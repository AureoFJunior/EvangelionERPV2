using Asp.Versioning;
using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Security;
using EvangelionERPV2.Web.Security.Iam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Serilog;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

namespace EvangelionERPV2.Web.Controllers
{
    [ApiController]
    [Authorize]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/ai")]
    public class AiController : ControllerBase
    {
        private const int MaxChatRequestBodySizeInBytes = 16 * 1024;
        private const int MaxAudioRequestBodySizeInBytes = 5 * 1024 * 1024;
        private const int MaxConversationsListed = 20;
        private const int PreviewLength = 80;

        private readonly IAiAgentService _agentService;
        private readonly IAiPendingActionService _pendingActions;
        private readonly IAiSpeechService _speech;
        private readonly IAiVoiceService _voice;
        private readonly IRepository<AiConversation> _conversations;
        private readonly IRepository<AiMessage> _messages;
        private readonly IRepository<User> _users;
        private readonly IPermissionEvaluator? _permissionEvaluator;
        private readonly AiSettings _settings;

        public AiController(
            IAiAgentService agentService,
            IAiPendingActionService pendingActions,
            IAiSpeechService speech,
            IAiVoiceService voice,
            IRepository<AiConversation> conversations,
            IRepository<AiMessage> messages,
            IRepository<User> users,
            IOptions<AiSettings> settings,
            IPermissionEvaluator? permissionEvaluator = null)
        {
            _agentService = agentService;
            _pendingActions = pendingActions;
            _speech = speech;
            _voice = voice;
            _conversations = conversations;
            _messages = messages;
            _users = users;
            _permissionEvaluator = permissionEvaluator;
            _settings = settings.Value;
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Ai.Chat)]
        [HttpPost("chat")]
        [RequestSizeLimit(MaxChatRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(AiChatResponseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Chat([FromBody] AiChatRequestDTO request, CancellationToken cancellation)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!_settings.Enabled)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "O assistente está desativado.");

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId is null)
                return Unauthorized();

            var user = await _users.GetByIdAsync(userId.Value);
            if (user is null)
                return Unauthorized();

            var conversation = await ResolveConversationAsync(request.ConversationId, enterpriseId, userId.Value);

            var granted = await GrantedPermissionsAsync(user);

            var result = await _agentService.RunTurnAsync(
                new AiTurnRequest(
                    conversation.Id,
                    request.Message,
                    granted,
                    new AiToolContext(enterpriseId, userId.Value),
                    request.Spoken),
                cancellation);

            Log.Logger.Information(
                "AI turn completed. Tools={Tools} PromptTokens={PromptTokens} CompletionTokens={CompletionTokens} LatencyMs={LatencyMs}",
                string.Join(",", result.ToolsCalled), result.PromptTokens, result.CompletionTokens, result.LatencyMs);

            return Ok(new AiChatResponseDTO
            {
                ConversationId = conversation.Id,
                Text = result.Text,
                ToolsCalled = result.ToolsCalled,
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens,
                LatencyMs = result.LatencyMs,
                PendingActions = result.PendingActions.Select(ToDto).ToList(),
            });
        }

        /// <summary>
        /// One spoken utterance in, its text out. The audio is read straight off the request body
        /// and forwarded to the transcription server — it is never bound as an <c>IFormFile</c>,
        /// because ASP.NET Core spools anything over 64 KB of a bound file to a temp file on disk,
        /// and this feature's promise is that no recording is ever written down.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Ai.Chat)]
        [HttpPost("transcribe")]
        [DisableFormValueModelBinding]
        [RequestSizeLimit(MaxAudioRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(AiTranscriptDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Transcribe(CancellationToken cancellation)
        {
            if (!_settings.Enabled || !_settings.SpeechEnabled)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "O modo de voz está desativado.");

            if (!TryGetEnterpriseId(out _) || TryGetUserId() is null)
                return Unauthorized();

            if (Request.ContentLength > _settings.MaxAudioBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge, "Áudio longo demais.");

            if (!MultipartRequestHelper.IsMultipart(Request.ContentType, out var boundary))
                return BadRequest("Envie o áudio como multipart/form-data.");

            var reader = new MultipartReader(boundary, Request.Body);
            var section = await reader.ReadNextSectionAsync(cancellation);

            while (section is not null)
            {
                if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                    && disposition.DispositionType.Equals("form-data")
                    && !string.IsNullOrEmpty(disposition.FileName.Value))
                {
                    await using var bounded = new BoundedReadStream(section.Body, _settings.MaxAudioBytes);

                    var result = await _speech.TranscribeAsync(
                        bounded,
                        section.ContentType ?? "application/octet-stream",
                        disposition.FileName.Value.Trim('"'),
                        cancellation);

                    if (bounded.ExceededLimit)
                        return StatusCode(StatusCodes.Status413PayloadTooLarge, "Áudio longo demais.");

                    if (!result.Success)
                        return StatusCode(StatusCodes.Status503ServiceUnavailable, result.Error);

                    Log.Logger.Information(
                        "Speech transcribed. Bytes={Bytes} LatencyMs={LatencyMs} HasSpeech={HasSpeech}",
                        bounded.BytesRead, result.Transcript.LatencyMs, result.Transcript.HasSpeech);

                    return Ok(new AiTranscriptDTO
                    {
                        Text = result.Transcript.Text,
                        LatencyMs = result.Transcript.LatencyMs,
                    });
                }

                section = await reader.ReadNextSectionAsync(cancellation);
            }

            return BadRequest("Nenhum áudio foi enviado.");
        }

        /// <summary>
        /// Reads one sentence aloud, so every platform hears the same voice instead of whatever the
        /// machine happens to have installed — on a Linux desktop without speech-dispatcher, that is
        /// nothing at all.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Ai.Chat)]
        [HttpPost("speak")]
        [RequestSizeLimit(MaxChatRequestBodySizeInBytes)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Speak([FromBody] AiSpeakRequestDTO request, CancellationToken cancellation)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!_settings.Enabled || !_settings.VoiceEnabled)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "A voz do assistente está desativada.");

            if (!TryGetEnterpriseId(out _) || TryGetUserId() is null)
                return Unauthorized();

            var text = request.Text?.Trim() ?? string.Empty;

            if (text.Length == 0)
                return BadRequest("Nada a falar.");

            if (text.Length > _settings.MaxSpeechChars)
                return BadRequest("Texto longo demais para uma frase.");

            var result = await _voice.SynthesizeAsync(text, cancellation);

            if (!result.Success || result.Audio is null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, result.Error);

            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            Response.Headers.Pragma = "no-cache";

            return new FileStreamResult(result.Audio, result.ContentType);
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Ai.Chat)]
        [HttpGet("conversations")]
        [ProducesResponseType(typeof(IEnumerable<AiConversationSummaryDTO>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Conversations()
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId is null)
                return Unauthorized();

            var conversations = await _conversations.GetAllAsync(
                conversation => conversation.EnterpriseId == enterpriseId && conversation.UserId == userId.Value);

            var recent = conversations
                .OrderByDescending(conversation => conversation.CreatedAt)
                .Take(MaxConversationsListed)
                .ToList();

            var previews = await PreviewsAsync(recent.Select(conversation => conversation.Id).ToList());

            return Ok(recent.Select(conversation => new AiConversationSummaryDTO
            {
                Id = conversation.Id,
                Title = conversation.Title,
                CreatedAt = conversation.CreatedAt,
                Preview = previews.TryGetValue(conversation.Id, out var preview) ? preview : string.Empty,
            }));
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Ai.Chat)]
        [HttpGet("conversations/{conversationId:guid}/messages")]
        [ProducesResponseType(typeof(IEnumerable<AiMessageDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Messages(Guid conversationId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId is null)
                return Unauthorized();

            var conversation = await _conversations.GetByIdAsync(conversationId);

            if (conversation is null || conversation.UserId != userId.Value || conversation.EnterpriseId != enterpriseId)
                return NotFound();

            var messages = await _messages.GetAllAsync(message => message.AiConversationId == conversationId);

            return Ok(messages
                .Where(message => message.Role == AiMessageRole.User || message.Role == AiMessageRole.Assistant)
                .OrderBy(message => message.CreatedAt)
                .Select(message => new AiMessageDTO
                {
                    Id = message.Id,
                    Role = message.Role,
                    Content = message.Content,
                    CreatedAt = message.CreatedAt,
                    ToolsCalled = ParseTools(message.ToolCallsJson),
                }));
        }

        private async Task<Dictionary<Guid, string>> PreviewsAsync(IReadOnlyCollection<Guid> conversationIds)
        {
            if (conversationIds.Count == 0)
                return new Dictionary<Guid, string>();

            var messages = await _messages.GetAllAsync(
                message => conversationIds.Contains(message.AiConversationId) && message.Role == AiMessageRole.User);

            return messages
                .GroupBy(message => message.AiConversationId)
                .ToDictionary(
                    group => group.Key,
                    group => Truncate(group.OrderBy(message => message.CreatedAt).First().Content));
        }

        private static string Truncate(string text) =>
            text.Length <= PreviewLength ? text : text[..PreviewLength] + "...";

        private static IReadOnlyList<string> ParseTools(string toolCallsJson)
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(toolCallsJson) ?? new List<string>();
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Ai.Chat)]
        [HttpGet("conversations/{conversationId:guid}/actions")]
        [ProducesResponseType(typeof(IEnumerable<AiPendingActionDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> PendingActions(Guid conversationId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId is null)
                return Unauthorized();

            var conversation = await _conversations.GetByIdAsync(conversationId);
            if (conversation is null || conversation.UserId != userId.Value || conversation.EnterpriseId != enterpriseId)
                return NotFound();

            var pending = await _pendingActions.PendingAsync(conversationId);

            return Ok(pending.Select(ToDto).ToList());
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Ai.Act)]
        [HttpPost("actions/{actionId:guid}/confirm")]
        [ProducesResponseType(typeof(AiActionOutcomeDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ConfirmAction(Guid actionId, CancellationToken cancellation)
        {
            if (!_settings.Enabled)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "O assistente está desativado.");

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId is null)
                return Unauthorized();

            var user = await _users.GetByIdAsync(userId.Value);
            if (user is null)
                return Unauthorized();

            var granted = await GrantedPermissionsAsync(user);

            var outcome = await _pendingActions.ConfirmAsync(
                actionId, granted, new AiToolContext(enterpriseId, userId.Value), cancellation);

            Log.Logger.Information(
                "AI pending action confirm requested. Action={ActionId} Success={Success}", actionId, outcome.Success);

            return outcome.Success
                ? Ok(new AiActionOutcomeDTO { Success = true, Message = outcome.Message })
                : BadRequest(new AiActionOutcomeDTO { Success = false, Message = outcome.Message });
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Ai.Act)]
        [HttpPost("actions/{actionId:guid}/reject")]
        [ProducesResponseType(typeof(AiActionOutcomeDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> RejectAction(Guid actionId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId is null)
                return Unauthorized();

            var outcome = await _pendingActions.RejectAsync(actionId, new AiToolContext(enterpriseId, userId.Value));

            return outcome.Success
                ? Ok(new AiActionOutcomeDTO { Success = true, Message = outcome.Message })
                : BadRequest(new AiActionOutcomeDTO { Success = false, Message = outcome.Message });
        }

        private async Task<HashSet<string>> GrantedPermissionsAsync(User user)
        {
            if (_permissionEvaluator == null)
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var effective = await _permissionEvaluator.EffectiveAsync(User, user);
            return effective.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static AiPendingActionDTO ToDto(AiPendingActionCard card) =>
            new()
            {
                Id = card.Id,
                ToolName = card.ToolName,
                Summary = card.Summary,
                ExpiresAt = card.ExpiresAt,
            };

        private async Task<AiConversation> ResolveConversationAsync(Guid? conversationId, Guid enterpriseId, Guid userId)
        {
            if (conversationId is not null)
            {
                var existing = await _conversations.GetByIdAsync(conversationId.Value);
                if (existing is not null && existing.UserId == userId)
                    return existing;
            }

            var created = await _conversations.CreateAsync(new AiConversation
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                UserId = userId,
                Title = string.Empty,
            });
            await _conversations.CommitAsync();
            return created;
        }

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }

        private Guid? TryGetUserId()
        {
            var claimValue = User.FindFirst(ClaimTypes.Sid)?.Value
                             ?? User.FindFirst("uid")?.Value;

            if (Guid.TryParse(claimValue, out var userId) && userId != Guid.Empty)
                return userId;

            return null;
        }
    }
}
