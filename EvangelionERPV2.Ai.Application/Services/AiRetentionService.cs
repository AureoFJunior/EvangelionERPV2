using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Options;

namespace EvangelionERPV2.AiModule.Application.Services
{
    /// <summary>
    /// Deletes conversations older than <see cref="AiSettings.MessageRetentionDays"/>.
    /// </summary>
    public sealed class AiRetentionService : IAiRetentionService
    {
        private readonly IRepository<AiMessage> _messages;
        private readonly IRepository<AiConversation> _conversations;
        private readonly IRepository<AiPendingAction> _actions;
        private readonly AiSettings _settings;

        public AiRetentionService(
            IRepository<AiMessage> messages,
            IRepository<AiConversation> conversations,
            IRepository<AiPendingAction> actions,
            IOptions<AiSettings> settings)
        {
            _messages = messages;
            _conversations = conversations;
            _actions = actions;
            _settings = settings?.Value ?? new AiSettings();
        }

        public async Task<int> PurgeAsync(CancellationToken cancellation = default)
        {
            if (_settings.MessageRetentionDays <= 0)
                return 0;

            var cutoff = DateTime.UtcNow.AddDays(-_settings.MessageRetentionDays);

            var stale = await OrEmptyAsync(() => _conversations.GetAllAsync(
                conversation => conversation.CreatedAt < cutoff));

            if (stale.Count == 0)
                return 0;

            foreach (var conversation in stale)
            {
                cancellation.ThrowIfCancellationRequested();

                var messages = await OrEmptyAsync(() => _messages.GetAllAsync(
                    message => message.AiConversationId == conversation.Id));

                var actions = await OrEmptyAsync(() => _actions.GetAllAsync(
                    action => action.AiConversationId == conversation.Id));

                if (actions.Count > 0)
                    _actions.DeleteRange(actions);

                if (messages.Count > 0)
                    _messages.DeleteRange(messages);

                _conversations.Delete(conversation);
            }

            await _conversations.CommitAsync(cancellation);

            return stale.Count;
        }

        /// <summary>The repositories throw when a read finds nothing rather than answering empty.</summary>
        private static async Task<IReadOnlyList<T>> OrEmptyAsync<T>(Func<Task<IEnumerable<T>>> query)
        {
            try
            {
                return (await query()).ToList();
            }
            catch (NotFoundDatabaseException)
            {
                return Array.Empty<T>();
            }
        }
    }
}
