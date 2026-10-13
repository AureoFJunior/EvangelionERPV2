using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Options;
using Moq;
using System.Linq.Expressions;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    /// <summary>
    /// Retention, which the module advertised for months and never applied.
    /// </summary>
    public class AiRetentionServiceTests
    {
        [Fact]
        public async Task AConversationPastTheWindow_IsDeletedWithEverythingUnderIt()
        {
            var world = new World(retentionDays: 90);
            var old = world.AddConversation(DateTime.UtcNow.AddDays(-91), messages: 3, actions: 1);

            Assert.Equal(1, await world.Service.PurgeAsync());

            Assert.DoesNotContain(world.Conversations, x => x.Id == old);
            Assert.Empty(world.Messages);
            Assert.Empty(world.Actions);
        }

        [Fact]
        public async Task AConversationInsideTheWindow_IsLeftAlone()
        {
            var world = new World(retentionDays: 90);
            world.AddConversation(DateTime.UtcNow.AddDays(-89), messages: 2);

            Assert.Equal(0, await world.Service.PurgeAsync());
            Assert.Single(world.Conversations);
            Assert.Equal(2, world.Messages.Count);
        }

        [Fact]
        public async Task OnlyTheOldConversationGoes()
        {
            var world = new World(retentionDays: 30);
            world.AddConversation(DateTime.UtcNow.AddDays(-31), messages: 1);
            var kept = world.AddConversation(DateTime.UtcNow.AddDays(-1), messages: 1);

            Assert.Equal(1, await world.Service.PurgeAsync());

            Assert.Equal(kept, Assert.Single(world.Conversations).Id);
            Assert.Single(world.Messages);
        }

        [Fact]
        public async Task RetentionOfZero_DeletesNothing()
        {
            var world = new World(retentionDays: 0);
            world.AddConversation(DateTime.UtcNow.AddYears(-5), messages: 4);

            Assert.Equal(0, await world.Service.PurgeAsync());
            Assert.Single(world.Conversations);
        }

        [Fact]
        public async Task NothingToPurge_TouchesNothing()
        {
            var world = new World(retentionDays: 90);

            Assert.Equal(0, await world.Service.PurgeAsync());
            world.ConversationRepository.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        private sealed class World
        {
            public List<AiConversation> Conversations { get; } = [];
            public List<AiMessage> Messages { get; } = [];
            public List<AiPendingAction> Actions { get; } = [];

            public Mock<IRepository<AiConversation>> ConversationRepository { get; } = new();

            public AiRetentionService Service { get; }

            public World(int retentionDays)
            {
                var messages = new Mock<IRepository<AiMessage>>();
                var actions = new Mock<IRepository<AiPendingAction>>();

                ConversationRepository
                    .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<AiConversation, bool>>>()))
                    .ReturnsAsync((Expression<Func<AiConversation, bool>> predicate) =>
                    {
                        var matched = Conversations.Where(predicate.Compile()).ToList();
                        return matched.Count > 0 ? matched : throw new NotFoundDatabaseException("none");
                    });

                ConversationRepository
                    .Setup(x => x.Delete(It.IsAny<AiConversation>()))
                    .Returns((AiConversation row) =>
                    {
                        Conversations.Remove(row);
                        return row;
                    });

                messages
                    .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<AiMessage, bool>>>()))
                    .ReturnsAsync((Expression<Func<AiMessage, bool>> predicate) =>
                        Messages.Where(predicate.Compile()).ToList());

                messages
                    .Setup(x => x.DeleteRange(It.IsAny<IEnumerable<AiMessage>>()))
                    .Returns((IEnumerable<AiMessage> rows) =>
                    {
                        var removed = rows.ToList();
                        foreach (var row in removed) Messages.Remove(row);
                        return removed;
                    });

                actions
                    .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<AiPendingAction, bool>>>()))
                    .ReturnsAsync((Expression<Func<AiPendingAction, bool>> predicate) =>
                        Actions.Where(predicate.Compile()).ToList());

                actions
                    .Setup(x => x.DeleteRange(It.IsAny<IEnumerable<AiPendingAction>>()))
                    .Returns((IEnumerable<AiPendingAction> rows) =>
                    {
                        var removed = rows.ToList();
                        foreach (var row in removed) Actions.Remove(row);
                        return removed;
                    });

                Service = new AiRetentionService(
                    messages.Object,
                    ConversationRepository.Object,
                    actions.Object,
                    Options.Create(new AiSettings { MessageRetentionDays = retentionDays }));
            }

            public Guid AddConversation(DateTime createdAt, int messages = 0, int actions = 0)
            {
                var conversation = new AiConversation
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = Guid.NewGuid(),
                    UserId = Guid.NewGuid(),
                    CreatedAt = createdAt,
                    IsActive = true,
                };

                Conversations.Add(conversation);

                for (var i = 0; i < messages; i++)
                {
                    Messages.Add(new AiMessage
                    {
                        Id = Guid.NewGuid(),
                        AiConversationId = conversation.Id,
                        Role = "user",
                        Content = "quanto o cliente Maria Silva deve?",
                        CreatedAt = createdAt,
                    });
                }

                for (var i = 0; i < actions; i++)
                {
                    Actions.Add(new AiPendingAction
                    {
                        Id = Guid.NewGuid(),
                        AiConversationId = conversation.Id,
                        ToolName = "ajustar_estoque",
                        CreatedAt = createdAt,
                    });
                }

                return conversation.Id;
            }
        }
    }
}
