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
using System.Net.Sockets;

namespace EvangelionERPV2.AiModule.Test.Ai
{
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
            chat.Verify(x => x.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ATurnThatCallsATool_RunsItThenAnswers()
        {
            var chat = new Mock<IChatClient>();
            chat.SetupSequence(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Returns(Stream(ToolCall("c1", "ferramenta_ok")))
                .Returns(Stream(Text("Você tem 12 unidades.")));

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

            chat.Verify(x => x.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
                Times.Exactly(3));
            Assert.Contains("não consegui", result.Text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task AToolThatThrows_BecomesAToolResultRatherThanAFailedTurn()
        {
            var chat = new Mock<IChatClient>();
            chat.SetupSequence(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Returns(Stream(ToolCall("c1", "ferramenta_ruim")))
                .Returns(Stream(Text("Não consegui consultar isso agora.")));

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

        [Fact]
        public async Task ABrandNewConversationWithNoHistory_StillAnswers()
        {
            var chat = ChatReturning(Text("Olá."));
            List<ChatMessage>? sent = null;
            chat.Setup(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => sent = m.ToList())
                .Returns(Stream(Text("Olá.")));

            var result = await BuildService(chat.Object, NoTools()).RunTurnAsync(Request("oi"), CancellationToken.None);

            Assert.Equal("Olá.", result.Text);
            Assert.NotNull(sent);
            Assert.Equal(new[] { ChatRole.System, ChatRole.User }, sent!.Select(x => x.Role));
            Assert.Equal("oi", sent[1].Text);
        }

        [Fact]
        public async Task AHistoryReadThatThrowsNotFound_IsTreatedAsNoHistory()
        {
            var messages = MessageRepository();
            messages.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<AiMessage, bool>>>()))
                .ThrowsAsync(new NotFoundDatabaseException("no rows"));

            var service = BuildService(ChatReturning(Text("Olá.")).Object, NoTools(), messages: messages);

            var result = await service.RunTurnAsync(Request("oi"), CancellationToken.None);

            Assert.Equal("Olá.", result.Text);
        }

        [Fact]
        public async Task ATurn_PersistsTheQuestionAndTheAnswerWithWhatTheyCost()
        {
            var response = Text("Você tem 12 unidades.");
            response.Usage = new UsageDetails { InputTokenCount = 900, OutputTokenCount = 30 };
            var persisted = new List<AiMessage>();

            await BuildService(ChatReturning(response).Object, NoTools(), persisted: persisted)
                .RunTurnAsync(Request("quantas coca tem?"), CancellationToken.None);

            var user = Assert.Single(persisted, x => x.Role == AiMessageRole.User);
            Assert.Equal("quantas coca tem?", user.Content);
            Assert.Equal(ConversationId, user.AiConversationId);

            var assistant = Assert.Single(persisted, x => x.Role == AiMessageRole.Assistant);
            Assert.Equal("Você tem 12 unidades.", assistant.Content);
            Assert.Equal(ConversationId, assistant.AiConversationId);
            Assert.Equal("modelo-de-teste", assistant.Model);
            Assert.Equal(900, assistant.PromptTokens);
            Assert.Equal(30, assistant.CompletionTokens);
            Assert.True(assistant.LatencyMs >= 0);
            Assert.Equal("[]", assistant.ToolCallsJson);
        }

        [Fact]
        public async Task ATurnStoppedAtTheCap_PersistsTheToolsItBurnedThrough()
        {
            var persisted = new List<AiMessage>();

            await BuildService(
                    ChatReturning(ToolCall("c1", "ferramenta_ok")).Object,
                    OneToolNamed("ferramenta_ok", () => "{}"),
                    maxIterations: 2,
                    persisted: persisted)
                .RunTurnAsync(Request("loop"), CancellationToken.None);

            var assistant = Assert.Single(persisted, x => x.Role == AiMessageRole.Assistant);
            Assert.Contains("não consegui", assistant.Content, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("[\"ferramenta_ok\",\"ferramenta_ok\"]", assistant.ToolCallsJson);
        }

        [Fact]
        public async Task ATurnThatOverrunsItsBudget_AnswersInsteadOfThrowing()
        {
            var service = BuildService(ChatThatNeverAnswers().Object, NoTools(), timeoutSeconds: 1);

            var result = await service.RunTurnAsync(Request("demora"), CancellationToken.None);

            Assert.Contains("demorou demais", result.Text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ATurnThatOverrunsItsBudget_PersistsWhatTheUserWasTold()
        {
            var persisted = new List<AiMessage>();

            await BuildService(ChatThatNeverAnswers().Object, NoTools(), persisted: persisted, timeoutSeconds: 1)
                .RunTurnAsync(Request("demora"), CancellationToken.None);

            var assistant = Assert.Single(persisted, x => x.Role == AiMessageRole.Assistant);
            Assert.Contains("demorou demais", assistant.Content, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [MemberData(nameof(ModelServerFailures))]
        public async Task AModelServerThatDoesNotAnswer_TellsTheUserInsteadOfThrowing(Exception failure)
        {
            // Observed 2026-09-14 on POST /ai/chat: TaskCanceledException -> IOException ->
            // SocketException out of HttpClient, which matched no catch here and left as an
            // unhandled 500. A model that did not answer is an outcome to report, not a crash.
            var service = BuildService(ChatThatFailsWith(failure).Object, NoTools());

            var result = await service.RunTurnAsync(Request("oi"), CancellationToken.None);

            Assert.Contains("não conseguiu falar com o modelo", result.Text, StringComparison.OrdinalIgnoreCase);
        }

        public static TheoryData<Exception> ModelServerFailures() =>
        [
            // HttpClient's own timeout: cancelled, but by nobody's token.
            new TaskCanceledException("timeout", new IOException("connection reset", new SocketException(104))),
            new HttpRequestException("connection refused"),
            new IOException("the response ended prematurely"),
            new SocketException(111),
        ];

        [Fact]
        public async Task AModelServerFailure_PersistsWhatTheUserWasTold()
        {
            var persisted = new List<AiMessage>();
            var failure = new TaskCanceledException("timeout", new IOException("reset"));

            await BuildService(ChatThatFailsWith(failure).Object, NoTools(), persisted: persisted)
                .RunTurnAsync(Request("oi"), CancellationToken.None);

            var assistant = Assert.Single(persisted, x => x.Role == AiMessageRole.Assistant);
            Assert.Contains("não conseguiu falar com o modelo", assistant.Content, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ACallerWhoHangsUp_IsNotReportedAsAModelFailure()
        {
            // Nobody is waiting for this answer, and writing "tente de novo" into a conversation the
            // user closed would be a message to nobody.
            using var caller = new CancellationTokenSource();
            await caller.CancelAsync();

            var service = BuildService(ChatThatNeverAnswers().Object, NoTools());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.RunTurnAsync(Request("oi"), caller.Token));
        }

        [Fact]
        public async Task ATurnWithZeroTimeoutSeconds_HasNoBudgetAndAnswersNormally()
        {
            var service = BuildService(ChatReturning(Text("Bom dia.")).Object, NoTools(), timeoutSeconds: 0);

            var result = await service.RunTurnAsync(Request("oi"), CancellationToken.None);

            Assert.Equal("Bom dia.", result.Text);
        }

        [Fact]
        public async Task ATurnCancelledByItsCaller_PropagatesRatherThanReportingATimeout()
        {
            var service = BuildService(ChatThatNeverAnswers().Object, NoTools());
            using var caller = new CancellationTokenSource();
            await caller.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.RunTurnAsync(Request("cancela"), caller.Token));
        }

        [Fact]
        public async Task ATurnStoppedAtTheCap_StillChunksAndSignalsDone()
        {
            var notifier = new Mock<IAiProgressNotifier>();

            await BuildService(
                    ChatReturning(ToolCall("c1", "ferramenta_ok")).Object,
                    OneToolNamed("ferramenta_ok", () => "{}"),
                    maxIterations: 2,
                    notifier: notifier.Object)
                .RunTurnAsync(Request("loop"), CancellationToken.None);

            notifier.Verify(x => x.ChunkAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.AtLeastOnce);
            notifier.Verify(x => x.DoneAsync(It.IsAny<Guid>(), ConversationId), Times.Once);
        }

        [Fact]
        public async Task ATurnThatOverrunsItsBudget_StillSignalsDone()
        {
            var notifier = new Mock<IAiProgressNotifier>();

            await BuildService(ChatThatNeverAnswers().Object, NoTools(), notifier: notifier.Object, timeoutSeconds: 1)
                .RunTurnAsync(Request("demora"), CancellationToken.None);

            notifier.Verify(x => x.DoneAsync(It.IsAny<Guid>(), ConversationId), Times.Once);
        }

        [Fact]
        public async Task ANotifierThatThrowsOnEveryCall_StillLetsTheTurnReturnItsAnswer()
        {
            var notifier = new Mock<IAiProgressNotifier>();
            notifier.Setup(x => x.StatusAsync(It.IsAny<Guid>(), It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("hub down"));
            notifier.Setup(x => x.ChunkAsync(It.IsAny<Guid>(), It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("hub down"));
            notifier.Setup(x => x.DoneAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ThrowsAsync(new InvalidOperationException("hub down"));

            var chat = new Mock<IChatClient>();
            chat.SetupSequence(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Returns(Stream(ToolCall("c1", "ferramenta_ok")))
                .Returns(Stream(Text("Você tem 12 unidades.")));

            var service = BuildService(chat.Object, OneToolNamed("ferramenta_ok", () => "{}"), notifier: notifier.Object);

            var result = await service.RunTurnAsync(Request("quantas coca tem?"), CancellationToken.None);

            Assert.Equal("Você tem 12 unidades.", result.Text);
        }


        [Fact]
        public async Task AWriteToolIsProposed_NotExecuted()
        {
            var ran = false;
            var chat = new Mock<IChatClient>();
            chat.SetupSequence(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Returns(Stream(ToolCall("c1", "criar_cliente")))
                .Returns(Stream(Text("Precisa confirmar na tela.")));

            var registry = new Mock<IAiToolRegistry>();
            registry.Setup(x => x.Build(It.IsAny<IReadOnlySet<string>>(), It.IsAny<AiToolContext>()))
                .Returns(new[] { AIFunctionFactory.Create(() => { ran = true; return "{}"; }, "criar_cliente", "cria") });
            registry.Setup(x => x.Find("criar_cliente")).Returns(WriteTool("criar_cliente"));

            var card = new AiPendingActionCard(Guid.NewGuid(), "criar_cliente", "Cadastrar o cliente Ana", "{}", DateTime.UtcNow.AddMinutes(30));
            var pending = new Mock<IAiPendingActionService>();
            pending.Setup(x => x.ProposeAsync(
                    It.IsAny<Guid>(), It.IsAny<IAiTool>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(card);

            var service = BuildService(chat.Object, registry.Object, pendingActions: pending.Object);

            var result = await service.RunTurnAsync(Request("cria a cliente Ana"), CancellationToken.None);

            Assert.False(ran);
            Assert.Single(result.PendingActions);
            Assert.Equal("criar_cliente", result.PendingActions[0].ToolName);
            pending.Verify(x => x.ProposeAsync(
                It.IsAny<Guid>(), It.IsAny<IAiTool>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }


        [Fact]
        public async Task AWriteWithUnusableArguments_IsCorrectedInsteadOfBecomingACard()
        {
            var chat = new Mock<IChatClient>();
            chat.SetupSequence(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Returns(Stream(ToolCall("c1", "atualizar_produto")))
                .Returns(Stream(Text("Preciso do id completo.")));

            var tool = new Mock<IAiTool>();
            tool.SetupGet(x => x.Name).Returns("atualizar_produto");
            tool.SetupGet(x => x.RequiresConfirmation).Returns(true);
            tool.SetupGet(x => x.RequiredPermission).Returns("products.update");
            tool.Setup(x => x.PrepareAsync(
                    It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<AiToolContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AiToolArguments.Refused("nenhum produto encontrado com \"5ecc3fd5\"."));

            var registry = new Mock<IAiToolRegistry>();
            registry.Setup(x => x.Build(It.IsAny<IReadOnlySet<string>>(), It.IsAny<AiToolContext>()))
                .Returns(Array.Empty<AIFunction>());
            registry.Setup(x => x.Find("atualizar_produto")).Returns(tool.Object);

            var pending = new Mock<IAiPendingActionService>();

            var service = BuildService(chat.Object, registry.Object, pendingActions: pending.Object);

            var result = await service.RunTurnAsync(Request("muda o preço"), CancellationToken.None);

            Assert.Empty(result.PendingActions);
            pending.Verify(x => x.ProposeAsync(
                It.IsAny<Guid>(), It.IsAny<IAiTool>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static IAiTool WriteTool(string name)
        {
            var tool = new Mock<IAiTool>();
            tool.SetupGet(x => x.Name).Returns(name);
            tool.SetupGet(x => x.RequiresConfirmation).Returns(true);
            tool.SetupGet(x => x.RequiredPermission).Returns("customers.create");
            // A mocked interface does not run the default PrepareAsync, so it has to be stubbed:
            // arguments pass through untouched, which is what a tool with nothing to resolve does.
            tool.Setup(x => x.PrepareAsync(
                    It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<AiToolContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyDictionary<string, object?> args, AiToolContext _, CancellationToken _) =>
                    AiToolArguments.Ok(args));
            return tool.Object;
        }


        [Fact]
        public async Task TheAnswerArrivesInPieces_WhileItIsStillBeingWritten()
        {
            // Voice mode speaks each piece as it lands. If the whole answer only shows up at the
            // end, the first spoken word waits for the last generated one.
            var chunks = new List<string>();
            var notifier = new Mock<IAiProgressNotifier>();
            notifier.Setup(x => x.ChunkAsync(It.IsAny<Guid>(), It.IsAny<string>()))
                .Callback<Guid, string>((_, chunk) => chunks.Add(chunk))
                .Returns(Task.CompletedTask);

            var chat = new Mock<IChatClient>();
            chat.Setup(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Returns(() => Pieces("Você tem ", "nove mil ", "duzentos e vinte e seis reais."));

            var result = await BuildService(chat.Object, NoTools(), notifier: notifier.Object)
                .RunTurnAsync(Request("quanto tenho a receber?"), CancellationToken.None);

            Assert.Equal("Você tem nove mil duzentos e vinte e seis reais.", result.Text);
            Assert.Equal(3, chunks.Count);
            Assert.Equal("Você tem ", chunks[0]);
        }

        [Fact]
        public async Task AModelsThinkingNeverReachesTheClient()
        {
            // qwen3 streams its reasoning in a separate field. It is not the answer, and a speaker
            // reading it aloud would be worse than useless.
            var chunks = new List<string>();
            var notifier = new Mock<IAiProgressNotifier>();
            notifier.Setup(x => x.ChunkAsync(It.IsAny<Guid>(), It.IsAny<string>()))
                .Callback<Guid, string>((_, chunk) => chunks.Add(chunk))
                .Returns(Task.CompletedTask);

            var chat = new Mock<IChatClient>();
            chat.Setup(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Returns(() => WithThinking("O usuário quer saber o total", "Você tem dois produtos."));

            var result = await BuildService(chat.Object, NoTools(), notifier: notifier.Object)
                .RunTurnAsync(Request("quantos produtos?"), CancellationToken.None);

            Assert.DoesNotContain(chunks, chunk => chunk.Contains("usuário quer saber"));
            Assert.Contains("dois produtos", result.Text);
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> Pieces(params string[] parts)
        {
            foreach (var part in parts)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, part);
                await Task.Yield();
            }
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> WithThinking(string thinking, string answer)
        {
            // Anything that is not TextContent is not the answer.
            yield return new ChatResponseUpdate(ChatRole.Assistant, new AIContent[] { new TextReasoningContent(thinking) });
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, answer);
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
            chat.Setup(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Returns(() => Stream(response));
            return chat;
        }

        /// <summary>
        /// The turn streams every iteration, so a stubbed client has to answer in updates. Folding a
        /// whole <see cref="ChatResponse"/> back into updates keeps these tests written in terms of
        /// the answer rather than the transport.
        /// </summary>
        private static async IAsyncEnumerable<ChatResponseUpdate> Stream(ChatResponse response)
        {
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
                await Task.Yield();
            }
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> NeverEnds(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
        {
            await Task.Delay(Timeout.Infinite, token);
            yield break;
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

        private static Mock<IRepository<AiMessage>> MessageRepository()
        {
            var messages = new Mock<IRepository<AiMessage>>();
            messages.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<AiMessage, bool>>>()))
                .ReturnsAsync(Array.Empty<AiMessage>());
            messages.Setup(x => x.CreateAsync(It.IsAny<AiMessage>()))
                .ReturnsAsync((AiMessage m) => m);
            return messages;
        }

        private static AiAgentService BuildService(
            IChatClient chat,
            IAiToolRegistry registry,
            int maxIterations = 6,
            Mock<IRepository<AiMessage>>? messages = null,
            List<AiMessage>? persisted = null,
            IAiProgressNotifier? notifier = null,
            int timeoutSeconds = 120,
            IAiPendingActionService? pendingActions = null)
        {
            messages ??= MessageRepository();

            if (persisted is not null)
                messages.Setup(x => x.CreateAsync(It.IsAny<AiMessage>()))
                    .Callback<AiMessage>(persisted.Add)
                    .ReturnsAsync((AiMessage m) => m);

            var settings = Options.Create(new AiSettings
            {
                Enabled = true,
                Model = "modelo-de-teste",
                MaxToolIterations = maxIterations,
                TimeoutSeconds = timeoutSeconds,
            });

            return new AiAgentService(
                chat, registry, settings, messages.Object,
                notifier ?? Mock.Of<IAiProgressNotifier>(),
                pendingActions ?? Mock.Of<IAiPendingActionService>());
        }

        private static Mock<IChatClient> ChatThatFailsWith(Exception failure)
        {
            var chat = new Mock<IChatClient>();
            chat.Setup(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Returns(() => Throwing(failure));
            return chat;
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> Throwing(Exception failure)
        {
            await Task.Yield();
            throw failure;

#pragma warning disable CS0162 // Unreachable, and required: an iterator needs a yield to be one.
            yield break;
#pragma warning restore CS0162
        }

        private static Mock<IChatClient> ChatThatNeverAnswers()
        {
            var chat = new Mock<IChatClient>();
            chat.Setup(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Returns((IEnumerable<ChatMessage> _, ChatOptions? _, CancellationToken token) => NeverEnds(token));
            return chat;
        }
    }
}
