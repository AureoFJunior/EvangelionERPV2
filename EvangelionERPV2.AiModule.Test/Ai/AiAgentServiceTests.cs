using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using System.Linq.Expressions;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    /// <summary>
    /// The loop is the part with no natural ceiling: a local model that has lost the thread will
    /// call the same tool until something stops it, and a tool that throws must not take the whole
    /// turn with it. Both of those are tested here against a mocked IChatClient — no test in this
    /// suite may require a running Ollama.
    /// </summary>
    public class AiAgentServiceTests
    {
        private static readonly Guid Tenant = Guid.NewGuid();
        private static readonly Guid ConversationId = Guid.NewGuid();

        [Fact]
        public async Task ATurnWithNoToolCalls_AnswersInOneModelCall()
        {
            var chat = ChatReturning(Text("Bom dia. Em que posso ajudar?"));
            var service = BuildService(chat.Object, NoTools());

            var result = await service.RunTurnAsync(Request("oi"), CancellationToken.None);

            Assert.Equal("Bom dia. Em que posso ajudar?", result.Text);
            Assert.Empty(result.ToolsCalled);
            chat.Verify(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ATurnThatCallsATool_RunsItThenAnswers()
        {
            var chat = new Mock<IChatClient>();
            chat.SetupSequence(x => x.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ToolCall("c1", "ferramenta_ok"))
                .ReturnsAsync(Text("Você tem 12 unidades."));

            var service = BuildService(chat.Object, OneToolNamed("ferramenta_ok", () => "{\"total\":1}"));

            var result = await service.RunTurnAsync(Request("quantas coca tem?"), CancellationToken.None);

            Assert.Equal("Você tem 12 unidades.", result.Text);
            Assert.Equal(new[] { "ferramenta_ok" }, result.ToolsCalled);
        }

        [Fact]
        public async Task AModelThatNeverStops_IsStoppedAtTheIterationCap()
        {
            var chat = ChatReturning(ToolCall("c1", "ferramenta_ok"));
            var service = BuildService(chat.Object, OneToolNamed("ferramenta_ok", () => "{}"), maxIterations: 3);

            var result = await service.RunTurnAsync(Request("loop"), CancellationToken.None);

            chat.Verify(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
                Times.Exactly(3));
            Assert.Contains("não consegui", result.Text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task AToolThatThrows_BecomesAToolResultRatherThanAFailedTurn()
        {
            var chat = new Mock<IChatClient>();
            chat.SetupSequence(x => x.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ToolCall("c1", "ferramenta_ruim"))
                .ReturnsAsync(Text("Não consegui consultar isso agora."));

            var service = BuildService(
                chat.Object,
                OneToolNamed("ferramenta_ruim", () => throw new InvalidOperationException("boom")));

            var result = await service.RunTurnAsync(Request("quebra"), CancellationToken.None);

            Assert.Equal("Não consegui consultar isso agora.", result.Text);
        }

        [Fact]
        public async Task ATurn_RecordsWhatItCost()
        {
            var response = Text("pronto");
            response.Usage = new UsageDetails { InputTokenCount = 1200, OutputTokenCount = 40 };
            var service = BuildService(ChatReturning(response).Object, NoTools());

            var result = await service.RunTurnAsync(Request("oi"), CancellationToken.None);

            Assert.Equal(1200, result.PromptTokens);
            Assert.Equal(40, result.CompletionTokens);
        }

        /// <summary>
        /// The repositories here signal "nothing matched" by throwing, and a conversation on its
        /// first turn legitimately has no messages. That must not end the turn.
        /// </summary>
        [Fact]
        public async Task ABrandNewConversationWithNoHistory_StillAnswers()
        {
            var messages = new Mock<IRepository<AiMessage>>();
            messages.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<AiMessage, bool>>>()))
                .ThrowsAsync(new NotFoundDatabaseException("no rows"));
            messages.Setup(x => x.CreateAsync(It.IsAny<AiMessage>()))
                .ReturnsAsync((AiMessage m) => m);

            var service = BuildService(ChatReturning(Text("Olá.")).Object, NoTools(), messages: messages.Object);

            var result = await service.RunTurnAsync(Request("oi"), CancellationToken.None);

            Assert.Equal("Olá.", result.Text);
        }

        private static AiTurnRequest Request(string text) =>
            new(ConversationId, text, new HashSet<string> { "products.read" }, new AiToolContext(Tenant, Guid.NewGuid()));

        private static ChatResponse Text(string text) =>
            new(new ChatMessage(ChatRole.Assistant, text));

        private static ChatResponse ToolCall(string callId, string name) =>
            new(new ChatMessage(ChatRole.Assistant,
                new AIContent[] { new FunctionCallContent(callId, name, new Dictionary<string, object?>()) }));

        private static Mock<IChatClient> ChatReturning(ChatResponse response)
        {
            var chat = new Mock<IChatClient>();
            chat.Setup(x => x.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);
            return chat;
        }

        private static IAiToolRegistry NoTools()
        {
            var registry = new Mock<IAiToolRegistry>();
            registry.Setup(x => x.Build(It.IsAny<IReadOnlySet<string>>(), It.IsAny<AiToolContext>()))
                .Returns(Array.Empty<AIFunction>());
            return registry.Object;
        }

        private static IAiToolRegistry OneToolNamed(string name, Func<string> body)
        {
            var function = AIFunctionFactory.Create(body, name, "ferramenta de teste");
            var registry = new Mock<IAiToolRegistry>();
            registry.Setup(x => x.Build(It.IsAny<IReadOnlySet<string>>(), It.IsAny<AiToolContext>()))
                .Returns(new[] { function });
            return registry.Object;
        }

        private static AiAgentService BuildService(
            IChatClient chat,
            IAiToolRegistry registry,
            int maxIterations = 6,
            IRepository<AiMessage>? messages = null)
        {
            var conversations = new Mock<IRepository<AiConversation>>();
            conversations.Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync(new AiConversation { Id = ConversationId, EnterpriseId = Tenant });

            if (messages is null)
            {
                var stubbed = new Mock<IRepository<AiMessage>>();
                stubbed.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<AiMessage, bool>>>()))
                    .ReturnsAsync(Array.Empty<AiMessage>());
                stubbed.Setup(x => x.CreateAsync(It.IsAny<AiMessage>()))
                    .ReturnsAsync((AiMessage m) => m);
                messages = stubbed.Object;
            }

            var settings = Options.Create(new AiSettings
            {
                Enabled = true,
                Model = "modelo-de-teste",
                MaxToolIterations = maxIterations,
            });

            return new AiAgentService(chat, registry, settings, conversations.Object, messages);
        }
    }
}
