using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Serilog;
using System.Security.Claims;

namespace EvangelionERPV2.Web.Controllers
{
    [ApiController]
    [Authorize]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/ai")]
    public class AiController : ControllerBase
    {
        private const int MaxChatRequestBodySizeInBytes = 16 * 1024;

        private readonly IAiAgentService _agentService;
        private readonly IRepository<AiConversation> _conversations;
        private readonly IRepository<User> _users;
        private readonly AiSettings _settings;

        public AiController(
            IAiAgentService agentService,
            IRepository<AiConversation> conversations,
            IRepository<User> users,
            IOptions<AiSettings> settings)
        {
            _agentService = agentService;
            _conversations = conversations;
            _users = users;
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

            var granted = RbacPermissions.All
                .Where(permission => PdvTokenScope.Grants(User, user.AccessLevel, permission))
                .ToHashSet(StringComparer.Ordinal);

            var result = await _agentService.RunTurnAsync(
                new AiTurnRequest(
                    conversation.Id,
                    request.Message,
                    granted,
                    new AiToolContext(enterpriseId, userId.Value)),
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
            });
        }

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
