using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Services
{
    public sealed class AiPendingActionService : IAiPendingActionService
    {
        private readonly IRepository<AiPendingAction> _actions;
        private readonly IRepository<AiConversation> _conversations;
        private readonly IAiToolRegistry _registry;
        private readonly AiSettings _settings;

        public AiPendingActionService(
            IRepository<AiPendingAction> actions,
            IRepository<AiConversation> conversations,
            IAiToolRegistry registry,
            IOptions<AiSettings> settings)
        {
            _actions = actions;
            _conversations = conversations;
            _registry = registry;
            _settings = settings.Value;
        }

        public async Task<AiPendingActionCard> ProposeAsync(
            Guid conversationId,
            IAiTool tool,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellation = default)
        {
            var action = await _actions.CreateAsync(new AiPendingAction
            {
                Id = Guid.NewGuid(),
                AiConversationId = conversationId,
                ToolName = tool.Name,
                ArgumentsJson = JsonSerializer.Serialize(arguments, AiJson.Options),
                Summary = Shorten(tool.Summarize(arguments)),
                Status = AiPendingActionStatus.Pending,
                ExpiresAt = DateTime.UtcNow.AddMinutes(Math.Max(1, _settings.PendingActionTtlMinutes)),
            });
            await _actions.CommitAsync(cancellation);

            return ToCard(action);
        }

        public async Task<IReadOnlyList<AiPendingActionCard>> PendingAsync(Guid conversationId)
        {
            var now = DateTime.UtcNow;
            var actions = await _actions.GetAllAsync(action =>
                action.AiConversationId == conversationId
                && action.Status == AiPendingActionStatus.Pending
                && action.ExpiresAt > now);

            return actions
                .OrderBy(action => action.CreatedAt)
                .Select(ToCard)
                .ToList();
        }

        public async Task<AiPendingActionOutcome> ConfirmAsync(
            Guid actionId,
            IReadOnlySet<string> grantedPermissions,
            AiToolContext context,
            CancellationToken cancellation = default)
        {
            var action = await _actions.GetByIdAsync(actionId);
            if (action is null || !await BelongsToAsync(action, context))
                return new AiPendingActionOutcome(false, "Ação não encontrada.", "{}");

            if (action.Status != AiPendingActionStatus.Pending)
                return new AiPendingActionOutcome(false, $"Esta ação já está {Describe(action.Status)}.", action.ResultJson);

            if (action.ExpiresAt <= DateTime.UtcNow)
            {
                await TransitionAsync(action, AiPendingActionStatus.Expired, context.UserId, cancellation);
                return new AiPendingActionOutcome(false, "Esta ação expirou. Peça de novo ao assistente.", "{}");
            }

            var tool = _registry.Find(action.ToolName);
            if (tool is null || !grantedPermissions.Contains(tool.RequiredPermission)
                             || !grantedPermissions.Contains(AiPermissions.Act))
            {
                return new AiPendingActionOutcome(false, "Você não tem permissão para executar esta ação.", "{}");
            }

            var arguments = Deserialize(action.ArgumentsJson);
            var claim = await ClaimForExecutionAsync(action.Id, context.UserId, cancellation);
            if (!claim.Acquired || claim.Action is null)
                return new AiPendingActionOutcome(false,
                    $"Esta ação já está {Describe(claim.Action?.Status ?? action.Status)}.",
                    claim.Action?.ResultJson ?? action.ResultJson);

            action = claim.Action;

            try
            {
                var value = await tool.Create(context).InvokeAsync(new AIFunctionArguments(arguments), cancellation);
                var result = value?.ToString() ?? "{}";

                if (TryReadError(result, out var toolError))
                {
                    Log.Logger.Warning("AI pending action refused by its tool. Tool={Tool}", action.ToolName);
                    action.ResultJson = result;
                    await TransitionAsync(action, AiPendingActionStatus.Failed, context.UserId, cancellation);
                    return new AiPendingActionOutcome(false, toolError, result);
                }

                action.ResultJson = result;
                action.ExecutedAt = DateTime.UtcNow;
                await TransitionAsync(action, AiPendingActionStatus.Confirmed, context.UserId, cancellation);

                return new AiPendingActionOutcome(true, "Feito.", action.ResultJson);
            }
            catch (OperationCanceledException)
            {
                await TransitionAsync(action, AiPendingActionStatus.Failed, context.UserId, CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                Log.Logger.Error("AI pending action failed. Tool={Tool} ErrorType={ErrorType}", action.ToolName, ex.GetType().Name);
                await TransitionAsync(action, AiPendingActionStatus.Failed, context.UserId, CancellationToken.None);
                return new AiPendingActionOutcome(false, "Não consegui executar a ação. Tente de novo.", "{}");
            }
        }

        private async Task<(AiPendingAction? Action, bool Acquired)> ClaimForExecutionAsync(
            Guid actionId, Guid userId, CancellationToken cancellation)
        {
            AiPendingAction? current = null;
            var acquired = false;

            await _actions.ExecuteInTransactionAsync(async () =>
            {
                await _actions.LockForUpdateAsync(actionId, cancellation);
                current = await _actions.GetByIdAsync(actionId);

                if (current?.Status != AiPendingActionStatus.Pending)
                    return;

                if (current.ExpiresAt <= DateTime.UtcNow)
                {
                    current.Status = AiPendingActionStatus.Expired;
                }
                else
                {
                    current.Status = AiPendingActionStatus.Executing;
                    current.ConfirmedByUserId = userId;
                    acquired = true;
                }

                current.UpdatedAt = DateTime.UtcNow;
                _actions.Update(current);
                await _actions.CommitAsync(cancellation);
            }, cancellation);

            return (current, acquired);
        }

        public async Task<AiPendingActionOutcome> RejectAsync(Guid actionId, AiToolContext context)
        {
            var action = await _actions.GetByIdAsync(actionId);
            if (action is null || !await BelongsToAsync(action, context))
                return new AiPendingActionOutcome(false, "Ação não encontrada.", "{}");

            if (action.Status != AiPendingActionStatus.Pending)
                return new AiPendingActionOutcome(false, $"Esta ação já está {Describe(action.Status)}.", "{}");

            await TransitionAsync(action, AiPendingActionStatus.Rejected, context.UserId);
            return new AiPendingActionOutcome(true, "Descartada.", "{}");
        }

        public async Task<int> SweepExpiredAsync(CancellationToken cancellation = default)
        {
            var now = DateTime.UtcNow;
            var stale = (await _actions.GetAllAsync(action =>
                action.Status == AiPendingActionStatus.Pending && action.ExpiresAt <= now)).ToList();

            if (stale.Count == 0)
                return 0;

            foreach (var action in stale)
            {
                action.Status = AiPendingActionStatus.Expired;
                action.UpdatedAt = now;
                _actions.Update(action);
            }

            await _actions.CommitAsync(cancellation);
            return stale.Count;
        }

        private async Task<bool> BelongsToAsync(AiPendingAction action, AiToolContext context)
        {
            var conversation = await _conversations.GetByIdAsync(action.AiConversationId);
            return conversation is not null
                   && conversation.UserId == context.UserId
                   && conversation.EnterpriseId == context.EnterpriseId;
        }

        private async Task TransitionAsync(AiPendingAction action, string status, Guid userId, CancellationToken cancellation = default)
        {
            action.Status = status;
            action.ConfirmedByUserId = userId;
            action.UpdatedAt = DateTime.UtcNow;
            _actions.Update(action);
            await _actions.CommitAsync(cancellation);
        }

        private static AiPendingActionCard ToCard(AiPendingAction action) =>
            new(action.Id, action.ToolName, action.Summary, action.ArgumentsJson, action.ExpiresAt);

        /// <summary>True when the tool answered with an "erro" field, with its message.</summary>
        private static bool TryReadError(string result, out string message)
        {
            message = string.Empty;

            try
            {
                using var document = JsonDocument.Parse(result);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("erro", out var error))
                {
                    return false;
                }

                message = error.GetString() ?? "A ação não pôde ser executada.";
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static Dictionary<string, object?> Deserialize(string argumentsJson)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson, AiJson.Options)
                       ?? new Dictionary<string, object?>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, object?>();
            }
        }

        private static string Shorten(string summary) =>
            summary.Length <= 512 ? summary : summary[..512];

        private static string Describe(string status) => status switch
        {
            AiPendingActionStatus.Confirmed => "confirmada",
            AiPendingActionStatus.Rejected => "descartada",
            AiPendingActionStatus.Expired => "expirada",
            AiPendingActionStatus.Executing => "em execução",
            AiPendingActionStatus.Failed => "com falha",
            _ => "pendente",
        };
    }
}
