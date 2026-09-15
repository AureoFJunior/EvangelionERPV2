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
    /// <remarks>
    /// <para>
    /// The setting existed from the first day of this module and nothing ever read it, so every
    /// question and answer since then was kept for ever. That is not a tidiness problem: a turn
    /// holds whatever the tools returned — customer names, telephone numbers, cities, credit limits,
    /// what each person owes — plus whatever the user typed, which may include a CPF used as a
    /// search term. LGPD art. 15 and 16 do not allow keeping personal data past the purpose that
    /// justified collecting it, and "we have a retention setting" is not a defence when nothing
    /// applies it.
    /// </para>
    /// <para>
    /// A hard delete, not the usual soft one. A soft-deleted row is still the personal data, still
    /// in the backup, still readable by anything that forgets the filter — which is the opposite of
    /// what retention means.
    /// </para>
    /// </remarks>
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
            // Zero or less disables it rather than deleting everything — an operator who wants to
            // keep conversations should not lose them to a typo.
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

                // Children first: a message or a card orphaned by its conversation is the same
                // personal data with nothing left pointing at it.
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
