using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using System.Linq.Expressions;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    public class AiPendingActionTests
    {
        private static readonly Guid EnterpriseId = Guid.NewGuid();
        private static readonly Guid UserId = Guid.NewGuid();
        private static readonly Guid ConversationId = Guid.NewGuid();
        private static readonly AiToolContext Context = new(EnterpriseId, UserId);

        [Fact]
        public void Build_HidesAWriteToolFromACallerWithoutAiAct()
        {
            var registry = new AiToolRegistry(new[]
            {
                WriteTool("criar_cliente", "customers.create"),
                ReadTool("buscar_clientes", "customers.read"),
            });

            var tools = registry.Build(
                new HashSet<string> { "customers.create", "customers.read" }, Context);

            Assert.Single(tools);
            Assert.Equal("buscar_clientes", tools[0].Name);
        }

        [Fact]
        public void Build_ShowsAWriteToolOnceAiActIsGranted()
        {
            var registry = new AiToolRegistry(new[] { WriteTool("criar_cliente", "customers.create") });

            var tools = registry.Build(
                new HashSet<string> { "customers.create", AiPermissions.Act }, Context);

            Assert.Single(tools);
            Assert.Equal("criar_cliente", tools[0].Name);
        }

        [Fact]
        public async Task Confirm_RunsTheToolAndMarksTheActionConfirmed()
        {
            var tool = WriteTool("criar_cliente", "customers.create");
            var action = PendingAction(tool.Name);
            var (service, actions) = BuildService(action, tool);

            var outcome = await service.ConfirmAsync(action.Id, Granted("customers.create"), Context);

            Assert.True(outcome.Success);
            Assert.Equal(AiPendingActionStatus.Confirmed, action.Status);
            Assert.Equal(UserId, action.ConfirmedByUserId);
            Assert.NotNull(action.ExecutedAt);
            actions.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task Confirm_RefusesAnActionPastItsTtl()
        {
            var tool = WriteTool("criar_cliente", "customers.create");
            var action = PendingAction(tool.Name);
            action.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            var (service, _) = BuildService(action, tool);

            var outcome = await service.ConfirmAsync(action.Id, Granted("customers.create"), Context);

            Assert.False(outcome.Success);
            Assert.Equal(AiPendingActionStatus.Expired, action.Status);
        }

        [Fact]
        public async Task Confirm_RefusesACallerWhoLostThePermissionSinceProposing()
        {
            var tool = WriteTool("criar_cliente", "customers.create");
            var action = PendingAction(tool.Name);
            var (service, _) = BuildService(action, tool);

            var outcome = await service.ConfirmAsync(
                action.Id, new HashSet<string> { AiPermissions.Act }, Context);

            Assert.False(outcome.Success);
            Assert.Equal(AiPendingActionStatus.Pending, action.Status);
        }

        [Fact]
        public async Task Confirm_RefusesSomeoneElsesAction()
        {
            var tool = WriteTool("criar_cliente", "customers.create");
            var action = PendingAction(tool.Name);
            var (service, _) = BuildService(action, tool);

            var stranger = new AiToolContext(EnterpriseId, Guid.NewGuid());

            var outcome = await service.ConfirmAsync(action.Id, Granted("customers.create"), stranger);

            Assert.False(outcome.Success);
            Assert.Equal(AiPendingActionStatus.Pending, action.Status);
        }

        [Fact]
        public async Task Confirm_RefusesAnActionAlreadyAnswered()
        {
            var tool = WriteTool("criar_cliente", "customers.create");
            var action = PendingAction(tool.Name);
            action.Status = AiPendingActionStatus.Rejected;
            var (service, _) = BuildService(action, tool);

            var outcome = await service.ConfirmAsync(action.Id, Granted("customers.create"), Context);

            Assert.False(outcome.Success);
            Assert.Equal(AiPendingActionStatus.Rejected, action.Status);
        }

        [Fact]
        public async Task Reject_MarksTheActionRejectedWithoutRunningIt()
        {
            var ran = false;
            var tool = WriteTool("criar_cliente", "customers.create", () => ran = true);
            var action = PendingAction(tool.Name);
            var (service, _) = BuildService(action, tool);

            var outcome = await service.RejectAsync(action.Id, Context);

            Assert.True(outcome.Success);
            Assert.Equal(AiPendingActionStatus.Rejected, action.Status);
            Assert.False(ran);
        }

        [Fact]
        public async Task Sweep_ExpiresOnlyThePastTtlProposals()
        {
            var stale = PendingAction("criar_cliente");
            stale.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
            var fresh = PendingAction("criar_cliente");

            var (service, _) = BuildService(new[] { stale, fresh }, WriteTool("criar_cliente", "customers.create"));

            var expired = await service.SweepExpiredAsync();

            Assert.Equal(1, expired);
            Assert.Equal(AiPendingActionStatus.Expired, stale.Status);
            Assert.Equal(AiPendingActionStatus.Pending, fresh.Status);
        }

        [Fact]
        public async Task Propose_StoresTheArgumentsAndTheCardSummary()
        {
            var tool = WriteTool("criar_cliente", "customers.create");
            var (service, actions) = BuildService(Array.Empty<AiPendingAction>(), tool);

            AiPendingAction? created = null;
            actions.Setup(x => x.CreateAsync(It.IsAny<AiPendingAction>()))
                .Callback<AiPendingAction>(a => created = a)
                .ReturnsAsync((AiPendingAction a) => a);

            var card = await service.ProposeAsync(
                ConversationId, tool, new Dictionary<string, object?> { ["nome"] = "Ana" });

            Assert.NotNull(created);
            Assert.Equal(AiPendingActionStatus.Pending, created!.Status);
            Assert.Contains("Ana", created.ArgumentsJson);
            Assert.Equal("resumo de teste", card.Summary);
            Assert.True(card.ExpiresAt > DateTime.UtcNow);
        }

        [Fact]
        public async Task Confirm_WhenTheToolRefuses_DoesNotReportItAsDone()
        {
            var tool = RefusingTool("atualizar_produto", "customers.create", "produto não encontrado nesta empresa");
            var action = PendingAction(tool.Name);
            var (service, _) = BuildService(action, tool);

            var outcome = await service.ConfirmAsync(action.Id, Granted("customers.create"), Context);

            Assert.False(outcome.Success);
            Assert.Equal("produto não encontrado nesta empresa", outcome.Message);
            Assert.Equal(AiPendingActionStatus.Pending, action.Status);
            Assert.Null(action.ExecutedAt);
        }

        [Fact]
        public async Task Confirm_WhenTheToolSucceeds_KeepsReportingItAsDone()
        {
            var tool = WriteTool("criar_cliente", "customers.create");
            var action = PendingAction(tool.Name);
            var (service, _) = BuildService(action, tool);

            var outcome = await service.ConfirmAsync(action.Id, Granted("customers.create"), Context);

            Assert.True(outcome.Success);
            Assert.Equal("Feito.", outcome.Message);
        }

        private static IAiTool RefusingTool(string name, string permission, string error) =>
            new FakeTool(name, permission, true, null, $"{{\"erro\":\"{error}\"}}");

        private static HashSet<string> Granted(string permission) =>
            new() { permission, AiPermissions.Act };

        private static AiPendingAction PendingAction(string toolName) => new()
        {
            Id = Guid.NewGuid(),
            AiConversationId = ConversationId,
            ToolName = toolName,
            ArgumentsJson = "{\"nome\":\"Ana\"}",
            Summary = "resumo de teste",
            Status = AiPendingActionStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
        };

        private static (AiPendingActionService Service, Mock<IRepository<AiPendingAction>> Actions) BuildService(
            AiPendingAction action, IAiTool tool) => BuildService(new[] { action }, tool);

        private static (AiPendingActionService Service, Mock<IRepository<AiPendingAction>> Actions) BuildService(
            IReadOnlyList<AiPendingAction> stored, IAiTool tool)
        {
            var actions = new Mock<IRepository<AiPendingAction>>();
            actions.Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => stored.FirstOrDefault(a => a.Id == id)!);
            actions.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<AiPendingAction, bool>>>()))
                .ReturnsAsync((Expression<Func<AiPendingAction, bool>> predicate) =>
                    stored.Where(predicate.Compile()).ToList());
            actions.Setup(x => x.CreateAsync(It.IsAny<AiPendingAction>()))
                .ReturnsAsync((AiPendingAction a) => a);

            var conversations = new Mock<IRepository<AiConversation>>();
            conversations.Setup(x => x.GetByIdAsync(ConversationId))
                .ReturnsAsync(new AiConversation
                {
                    Id = ConversationId,
                    EnterpriseId = EnterpriseId,
                    UserId = UserId,
                });

            var service = new AiPendingActionService(
                actions.Object,
                conversations.Object,
                new AiToolRegistry(new[] { tool }),
                Options.Create(new AiSettings { Enabled = true, PendingActionTtlMinutes = 30 }));

            return (service, actions);
        }

        private static IAiTool ReadTool(string name, string permission) => new FakeTool(name, permission, false, null);

        private static IAiTool WriteTool(string name, string permission, Action? onRun = null) =>
            new FakeTool(name, permission, true, onRun);

        private sealed class FakeTool : IAiTool
        {
            private readonly Action? _onRun;
            private readonly string _payload;

            public FakeTool(string name, string permission, bool requiresConfirmation, Action? onRun, string? payload = null)
            {
                Name = name;
                RequiredPermission = permission;
                RequiresConfirmation = requiresConfirmation;
                _onRun = onRun;
                _payload = payload ?? "{\"ok\":true}";
            }

            public string Name { get; }
            public string Description => "ferramenta de teste";
            public string RequiredPermission { get; }
            public bool RequiresConfirmation { get; }

            public string Summarize(IReadOnlyDictionary<string, object?> arguments) => "resumo de teste";

            public AIFunction Create(AiToolContext context) =>
                AIFunctionFactory.Create(() =>
                {
                    _onRun?.Invoke();
                    return _payload;
                }, Name, Description);
        }
    }
}
