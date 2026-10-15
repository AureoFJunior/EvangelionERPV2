using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The assistant ships switched off, and the voice half is off again behind its own flag. These
    /// cover the gates in front of every route — the kill switches, the conversation ownership
    /// check, and the fact that a confirm re-reads the caller's permissions instead of trusting the
    /// turn that proposed the write.
    /// </summary>
    public class AiControllerGatesTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Mock<IAiAgentService> _agent = new();
        private readonly Mock<IAiPendingActionService> _pendingActions = new();
        private readonly Mock<IAiVoiceService> _voice = new();
        private readonly Mock<IRepository<AiConversation>> _conversations = new();
        private readonly Mock<IRepository<AiMessage>> _messages = new();
        private readonly Mock<IRepository<User>> _users = new();

        private AiController Controller(AiSettings? settings = null, bool knownUser = true)
        {
            if (knownUser)
            {
                _users.Setup(r => r.GetByIdAsync(_userId))
                    .ReturnsAsync(new User
                    {
                        Id = _userId,
                        EnterpriseId = _enterpriseId,
                        AccessLevel = (short)EnumAccessLevel.Admin,
                        IsActive = true
                    });
            }

            return Build(settings).WithTenant(_enterpriseId, _userId);
        }

        private AiController Anonymous(AiSettings? settings = null) => Build(settings).WithoutTenant();

        private AiController Build(AiSettings? settings) =>
            new(
                _agent.Object,
                _pendingActions.Object,
                Mock.Of<IAiSpeechService>(),
                _voice.Object,
                _conversations.Object,
                _messages.Object,
                _users.Object,
                Options.Create(settings ?? new AiSettings { Enabled = true, VoiceEnabled = true }),
                ControllerPolicyTestHelper.RolePermissionEvaluator());

        private static AiSettings Off() => new() { Enabled = false };

        private static void AssertServiceUnavailable(IActionResult result)
        {
            var status = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
        }

        /// <summary>
        /// `AiSettings.Enabled` is false by default and the module must stay silent until somebody
        /// turns it on — 503 rather than 404, because the route exists and the feature does not.
        /// </summary>
        [Fact]
        public async Task Chat_WhileTheAssistantIsOff_IsServiceUnavailable()
        {
            AssertServiceUnavailable(await Controller(Off()).Chat(
                new AiChatRequestDTO { Message = "oi" }, CancellationToken.None));

            _agent.Verify(a => a.RunTurnAsync(It.IsAny<AiTurnRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Chat_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().Chat(
                new AiChatRequestDTO { Message = "oi" }, CancellationToken.None));
        }

        /// <summary>
        /// The claim can name a user the table no longer has. The turn is refused rather than run
        /// with no permissions resolved, because the tools are filtered from that user's row.
        /// </summary>
        [Fact]
        public async Task Chat_WhenTheClaimNamesAUserThatIsGone_IsUnauthorized()
        {
            _users.Setup(r => r.GetByIdAsync(_userId)).ReturnsAsync((User)null!);

            Assert.IsType<UnauthorizedResult>(await Controller(knownUser: false).Chat(
                new AiChatRequestDTO { Message = "oi" }, CancellationToken.None));

            _agent.Verify(a => a.RunTurnAsync(It.IsAny<AiTurnRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// The tool context is built from the token, never from the body — a model that asked for
        /// another tenant would be given the caller's anyway.
        /// </summary>
        [Fact]
        public async Task Chat_BuildsTheToolContextFromTheTokenAndForwardsTheSpokenFlag()
        {
            AiTurnRequest? captured = null;
            _conversations.Setup(r => r.CreateAsync(It.IsAny<AiConversation>()))
                .ReturnsAsync((AiConversation c) => c);
            _agent.Setup(a => a.RunTurnAsync(It.IsAny<AiTurnRequest>(), It.IsAny<CancellationToken>()))
                .Callback<AiTurnRequest, CancellationToken>((request, _) => captured = request)
                .ReturnsAsync(new AiTurnResult("ok", [], 1, 1, 1, []));

            Assert.IsType<OkObjectResult>(await Controller().Chat(
                new AiChatRequestDTO { Message = "quanto vendi?", Spoken = true }, CancellationToken.None));

            Assert.Equal(_enterpriseId, captured!.Context.EnterpriseId);
            Assert.Equal(_userId, captured.Context.UserId);
            Assert.True(captured.Spoken);
        }

        [Fact]
        public async Task Speak_WhileTheAssistantIsOff_IsServiceUnavailable()
        {
            AssertServiceUnavailable(await Controller(Off()).Speak(
                new AiSpeakRequestDTO { Text = "olá" }, CancellationToken.None));
        }

        /// <summary>
        /// The voice is a second switch on top of the first: a tenant can have the assistant
        /// without having Piper running beside it.
        /// </summary>
        [Fact]
        public async Task Speak_WhileOnlyTheVoiceIsOff_IsServiceUnavailable()
        {
            var settings = new AiSettings { Enabled = true, VoiceEnabled = false };

            AssertServiceUnavailable(await Controller(settings).Speak(
                new AiSpeakRequestDTO { Text = "olá" }, CancellationToken.None));

            _voice.Verify(v => v.SynthesizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// The null case is reachable even though the DTO types <c>Text</c> as non-nullable: a body
        /// carrying <c>"text": null</c> deserialises to one, which is why the controller guards it.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task Speak_WithNothingToSay_IsRefused(string? text)
        {
            var result = Assert.IsType<BadRequestObjectResult>(await Controller().Speak(
                new AiSpeakRequestDTO { Text = text! }, CancellationToken.None));

            Assert.Equal("Nada a falar.", result.Value);
        }

        [Fact]
        public async Task Speak_WithMoreTextThanOneSentence_IsRefused()
        {
            var settings = new AiSettings { Enabled = true, VoiceEnabled = true, MaxSpeechChars = 10 };

            var result = Assert.IsType<BadRequestObjectResult>(await Controller(settings).Speak(
                new AiSpeakRequestDTO { Text = new string('a', 11) }, CancellationToken.None));

            Assert.Equal("Texto longo demais para uma frase.", result.Value);
        }

        [Fact]
        public async Task Speak_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().Speak(
                new AiSpeakRequestDTO { Text = "olá" }, CancellationToken.None));
        }

        [Fact]
        public async Task Speak_WhenTheVoiceServiceIsUnreachable_IsServiceUnavailable()
        {
            _voice.Setup(v => v.SynthesizeAsync("olá", It.IsAny<CancellationToken>()))
                .ReturnsAsync(AiVoiceResult.Failed("piper is down"));

            AssertServiceUnavailable(await Controller().Speak(
                new AiSpeakRequestDTO { Text = "olá" }, CancellationToken.None));
        }

        /// <summary>
        /// The audio is a machine reading text out loud and is never stored — the response is
        /// streamed straight through, and it says so in the headers so nothing caches it.
        /// </summary>
        [Fact]
        public async Task Speak_StreamsTheAudioAndForbidsCaching()
        {
            _voice.Setup(v => v.SynthesizeAsync("olá", It.IsAny<CancellationToken>()))
                .ReturnsAsync(AiVoiceResult.Ok(new MemoryStream([1, 2, 3]), "audio/wav", 12));

            var controller = Controller();
            var result = Assert.IsType<FileStreamResult>(await controller.Speak(
                new AiSpeakRequestDTO { Text = "olá" }, CancellationToken.None));

            Assert.Equal("audio/wav", result.ContentType);
            Assert.Contains("no-store", controller.Response.Headers.CacheControl.ToString());
        }

        [Fact]
        public async Task Conversations_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().Conversations());
        }

        /// <summary>
        /// A conversation belongs to one person, not one company: the list is scoped by tenant
        /// *and* by caller, so a colleague's questions never appear in somebody else's history.
        /// </summary>
        [Fact]
        public async Task Conversations_ScopesToTheCallerAndNotJustTheTenant()
        {
            System.Linq.Expressions.Expression<Func<AiConversation, bool>>? captured = null;
            _conversations.Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AiConversation, bool>>>()))
                .Callback<System.Linq.Expressions.Expression<Func<AiConversation, bool>>?>(p => captured = p)
                .ReturnsAsync([]);
            _messages.Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AiMessage, bool>>>()))
                .ReturnsAsync([]);

            Assert.IsType<OkObjectResult>(await Controller().Conversations());

            var matches = captured!.Compile();
            Assert.True(matches(new AiConversation { EnterpriseId = _enterpriseId, UserId = _userId }));
            Assert.False(matches(new AiConversation { EnterpriseId = _enterpriseId, UserId = Guid.NewGuid() }));
            Assert.False(matches(new AiConversation { EnterpriseId = Guid.NewGuid(), UserId = _userId }));
        }

        [Fact]
        public async Task Messages_ForAColleaguesConversation_IsNotFound()
        {
            var conversationId = Guid.NewGuid();
            _conversations.Setup(r => r.GetByIdAsync(conversationId))
                .ReturnsAsync(new AiConversation
                {
                    Id = conversationId,
                    EnterpriseId = _enterpriseId,
                    UserId = Guid.NewGuid()
                });

            Assert.IsType<NotFoundResult>(await Controller().Messages(conversationId));
        }

        [Fact]
        public async Task Messages_ForAnotherTenantsConversation_IsNotFound()
        {
            var conversationId = Guid.NewGuid();
            _conversations.Setup(r => r.GetByIdAsync(conversationId))
                .ReturnsAsync(new AiConversation
                {
                    Id = conversationId,
                    EnterpriseId = Guid.NewGuid(),
                    UserId = _userId
                });

            Assert.IsType<NotFoundResult>(await Controller().Messages(conversationId));
        }

        /// <summary>
        /// The transcript shown back is what a person said and what the assistant answered. The
        /// tool and system turns are working notes and stay out of it.
        /// </summary>
        [Fact]
        public async Task Messages_ReturnsOnlyTheUserAndAssistantTurns()
        {
            var conversationId = Guid.NewGuid();
            _conversations.Setup(r => r.GetByIdAsync(conversationId))
                .ReturnsAsync(new AiConversation
                {
                    Id = conversationId,
                    EnterpriseId = _enterpriseId,
                    UserId = _userId
                });
            _messages.Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AiMessage, bool>>>()))
                .ReturnsAsync(
                [
                    new AiMessage { AiConversationId = conversationId, Role = AiMessageRole.System, Content = "prompt" },
                    new AiMessage { AiConversationId = conversationId, Role = AiMessageRole.User, Content = "oi" },
                    new AiMessage { AiConversationId = conversationId, Role = AiMessageRole.Tool, Content = "{}" },
                    new AiMessage { AiConversationId = conversationId, Role = AiMessageRole.Assistant, Content = "olá" }
                ]);

            var result = Assert.IsType<OkObjectResult>(await Controller().Messages(conversationId));
            var messages = Assert.IsAssignableFrom<IEnumerable<AiMessageDTO>>(result.Value).ToList();

            Assert.Equal(2, messages.Count);
            Assert.DoesNotContain(messages, m => m.Role == AiMessageRole.System || m.Role == AiMessageRole.Tool);
        }

        [Fact]
        public async Task PendingActions_ForAColleaguesConversation_IsNotFound()
        {
            var conversationId = Guid.NewGuid();
            _conversations.Setup(r => r.GetByIdAsync(conversationId))
                .ReturnsAsync(new AiConversation
                {
                    Id = conversationId,
                    EnterpriseId = _enterpriseId,
                    UserId = Guid.NewGuid()
                });

            Assert.IsType<NotFoundResult>(await Controller().PendingActions(conversationId));
            _pendingActions.Verify(p => p.PendingAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task PendingActions_ForTheCallersOwnConversation_ListsTheCards()
        {
            var conversationId = Guid.NewGuid();
            _conversations.Setup(r => r.GetByIdAsync(conversationId))
                .ReturnsAsync(new AiConversation
                {
                    Id = conversationId,
                    EnterpriseId = _enterpriseId,
                    UserId = _userId
                });
            _pendingActions.Setup(p => p.PendingAsync(conversationId))
                .ReturnsAsync([new AiPendingActionCard(Guid.NewGuid(), "ajustar_estoque", "Ajustar", "{}", DateTime.UtcNow)]);

            var result = Assert.IsType<OkObjectResult>(await Controller().PendingActions(conversationId));

            Assert.Single(Assert.IsAssignableFrom<IEnumerable<AiPendingActionDTO>>(result.Value));
        }

        [Fact]
        public async Task ConfirmAction_WhileTheAssistantIsOff_IsServiceUnavailable()
        {
            AssertServiceUnavailable(await Controller(Off()).ConfirmAction(Guid.NewGuid(), CancellationToken.None));

            _pendingActions.Verify(
                p => p.ConfirmAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<AiToolContext>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// A card outlives the turn that proposed it, so the permissions handed to the confirm are
        /// re-derived from the caller's row *now* — a demotion between propose and confirm has to
        /// take effect.
        /// </summary>
        [Fact]
        public async Task ConfirmAction_ResolvesThePermissionsAtConfirmTimeFromTheCallersRow()
        {
            IReadOnlySet<string>? granted = null;
            _pendingActions.Setup(p => p.ConfirmAsync(
                    It.IsAny<Guid>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<AiToolContext>(), It.IsAny<CancellationToken>()))
                .Callback((Guid _, IReadOnlySet<string> permissions, AiToolContext _, CancellationToken _) =>
                    granted = permissions)
                .ReturnsAsync(new AiPendingActionOutcome(true, "Feito.", "{}"));

            Assert.IsType<OkObjectResult>(await Controller().ConfirmAction(Guid.NewGuid(), CancellationToken.None));

            Assert.NotNull(granted);
            Assert.NotEmpty(granted!);
        }

        /// <summary>
        /// A tool that refuses in its own payload is not a success. Answering 200 with "Feito."
        /// for one of those made a write that never happened read as done.
        /// </summary>
        [Fact]
        public async Task ConfirmAction_WhenTheToolRefused_AnswersBadRequestRatherThanDone()
        {
            _pendingActions.Setup(p => p.ConfirmAsync(
                    It.IsAny<Guid>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<AiToolContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AiPendingActionOutcome(false, "Produto não encontrado.", "{}"));

            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().ConfirmAction(Guid.NewGuid(), CancellationToken.None));

            Assert.False(Assert.IsType<AiActionOutcomeDTO>(result.Value).Success);
        }

        [Fact]
        public async Task ConfirmAction_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(
                await Anonymous().ConfirmAction(Guid.NewGuid(), CancellationToken.None));
        }

        [Fact]
        public async Task RejectAction_ScopesTheCardToTheCallersTenantAndUser()
        {
            var actionId = Guid.NewGuid();
            AiToolContext? context = null;
            _pendingActions.Setup(p => p.RejectAsync(actionId, It.IsAny<AiToolContext>()))
                .Callback((Guid _, AiToolContext ctx) => context = ctx)
                .ReturnsAsync(new AiPendingActionOutcome(true, "Descartado.", "{}"));

            Assert.IsType<OkObjectResult>(await Controller().RejectAction(actionId));

            Assert.Equal(_enterpriseId, context!.EnterpriseId);
            Assert.Equal(_userId, context.UserId);
        }

        [Fact]
        public async Task RejectAction_ForACardThatIsNotTheCallers_AnswersBadRequest()
        {
            _pendingActions.Setup(p => p.RejectAsync(It.IsAny<Guid>(), It.IsAny<AiToolContext>()))
                .ReturnsAsync(new AiPendingActionOutcome(false, "Ação não encontrada.", "{}"));

            Assert.IsType<BadRequestObjectResult>(await Controller().RejectAction(Guid.NewGuid()));
        }

        [Fact]
        public async Task RejectAction_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().RejectAction(Guid.NewGuid()));
        }
    }
}
