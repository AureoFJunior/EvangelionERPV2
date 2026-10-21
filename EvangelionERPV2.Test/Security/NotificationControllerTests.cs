using EvangelionERPV2.NotificationModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Hubs;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The bell reaches every screen in a company, so the two things that matter here are who a
    /// feed item is allowed to reach and how often the sweep is willing to wake everybody up.
    /// </summary>
    public class NotificationControllerTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Mock<INotificationService> _service = new();
        private readonly Mock<IRepository<User>> _users = new();
        private readonly Mock<IRepository<Enterprise>> _enterprises = new();
        private readonly Mock<IHubContext<OrderHub>> _hub = new();
        private readonly Mock<IDistributedCache> _cache = new();
        private readonly Mock<IClientProxy> _group = new();

        public NotificationControllerTests()
        {
            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(_group.Object);
            _hub.SetupGet(h => h.Clients).Returns(clients.Object);
        }

        private NotificationController Controller(IPermissionEvaluator? evaluator = null) =>
            new NotificationController(_service.Object, _users.Object, _enterprises.Object, _hub.Object, _cache.Object,
                evaluator ?? ControllerPolicyTestHelper.RolePermissionEvaluator())
                .WithTenant(_enterpriseId, _userId);

        private NotificationController Anonymous() =>
            new NotificationController(_service.Object, _users.Object, _enterprises.Object, _hub.Object, _cache.Object,
                ControllerPolicyTestHelper.RolePermissionEvaluator())
                .WithoutTenant();

        private void CallerIs(EnumAccessLevel level, Guid? enterpriseId = null, bool isActive = true) =>
            _users.Setup(r => r.GetByIdAsync(_userId))
                .ReturnsAsync(new User
                {
                    Id = _userId,
                    IsActive = isActive,
                    EnterpriseId = enterpriseId ?? _enterpriseId,
                    AccessLevel = (short)level
                });

        private static NotificationItemDTO Item(string source, string? permission = null, bool isRead = false) => new()
        {
            Id = Guid.NewGuid(),
            Source = source,
            RequiredPermission = permission,
            IsRead = isRead,
            Title = "Aviso"
        };

        [Fact]
        public async Task Feed_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().Feed());
        }

        /// <summary>
        /// A token can outlive the membership it was minted for, so the feed re-reads the user
        /// rather than trusting the claims it was handed.
        /// </summary>
        [Fact]
        public async Task Feed_WhenTheCallerNoLongerBelongsToTheTenant_IsUnauthorized()
        {
            CallerIs(EnumAccessLevel.Admin, enterpriseId: Guid.NewGuid());

            Assert.IsType<UnauthorizedResult>(await Controller().Feed());
        }

        [Fact]
        public async Task Feed_WhenTheCallerWasDeactivated_IsUnauthorized()
        {
            CallerIs(EnumAccessLevel.Admin, isActive: false);

            Assert.IsType<UnauthorizedResult>(await Controller().Feed());
        }

        /// <summary>
        /// An alert names the permission it reveals — "payables are overdue" tells a salesperson
        /// something the payables screen would refuse them. The feed drops what the caller could
        /// not have read directly.
        /// </summary>
        [Fact]
        public async Task Feed_DropsItemsTheCallerCouldNotReadDirectly()
        {
            CallerIs(EnumAccessLevel.Employee);

            _service.Setup(s => s.GetFeedAsync(_enterpriseId, _userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NotificationFeedDTO
                {
                    Items =
                    [
                        Item(NotificationSources.Broadcast),
                        Item(NotificationSources.Alert, "payablebills.read")
                    ]
                });

            var result = Assert.IsType<OkObjectResult>(await Controller().Feed());
            var feed = Assert.IsType<NotificationFeedDTO>(result.Value);

            Assert.Single(feed.Items);
            Assert.Equal(NotificationSources.Broadcast, feed.Items[0].Source);
        }

        [Fact]
        public async Task Feed_UsesIamPermissionsInsteadOfTheUsersRole()
        {
            CallerIs(EnumAccessLevel.Manager);
            _service.Setup(s => s.GetFeedAsync(_enterpriseId, _userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NotificationFeedDTO
                {
                    Items = [Item(NotificationSources.Broadcast), Item(NotificationSources.Alert, RbacPermissions.BankReconciliation.Read)]
                });
            var evaluator = new Mock<IPermissionEvaluator>();
            evaluator.Setup(x => x.EffectiveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<User>()))
                .ReturnsAsync((IReadOnlySet<string>)new HashSet<string>());

            var result = Assert.IsType<OkObjectResult>(await Controller(evaluator.Object).Feed());
            var feed = Assert.IsType<NotificationFeedDTO>(result.Value);

            Assert.Single(feed.Items);
            Assert.Null(feed.Items[0].RequiredPermission);
        }

        /// <summary>An item with no required permission reaches everyone — that is a broadcast.</summary>
        [Fact]
        public async Task Feed_KeepsItemsThatRequireNoPermission()
        {
            CallerIs(EnumAccessLevel.Employee);

            _service.Setup(s => s.GetFeedAsync(_enterpriseId, _userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NotificationFeedDTO
                {
                    Items = [Item(NotificationSources.Broadcast), Item(NotificationSources.Broadcast, isRead: true)]
                });

            var result = Assert.IsType<OkObjectResult>(await Controller().Feed());
            var feed = Assert.IsType<NotificationFeedDTO>(result.Value);

            Assert.Equal(2, feed.Items.Count);
        }

        /// <summary>
        /// The counts are recomputed after the filter, not taken from the service — otherwise the
        /// badge would advertise an item the caller cannot open.
        /// </summary>
        [Fact]
        public async Task Feed_RecomputesTheCountsAfterFiltering()
        {
            CallerIs(EnumAccessLevel.Employee);

            _service.Setup(s => s.GetFeedAsync(_enterpriseId, _userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NotificationFeedDTO
                {
                    ActiveAlertCount = 99,
                    UnreadBroadcastCount = 99,
                    Items =
                    [
                        Item(NotificationSources.Broadcast),
                        Item(NotificationSources.Broadcast, isRead: true),
                        Item(NotificationSources.Alert, "payablebills.read")
                    ]
                });

            var result = Assert.IsType<OkObjectResult>(await Controller().Feed());
            var feed = Assert.IsType<NotificationFeedDTO>(result.Value);

            Assert.Equal(0, feed.ActiveAlertCount);
            Assert.Equal(1, feed.UnreadBroadcastCount);
        }

        [Fact]
        public async Task MarkRead_ScopesTheRowToTheCallerAndTheirTenant()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.MarkReadAsync(id, _enterpriseId, _userId, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            Assert.IsType<NoContentResult>(await Controller().MarkRead(id));
            _service.VerifyAll();
        }

        [Fact]
        public async Task MarkRead_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().MarkRead(Guid.NewGuid()));
        }

        [Fact]
        public async Task MarkAllRead_AnswersHowManyItMarked()
        {
            _service.Setup(s => s.MarkAllReadAsync(_enterpriseId, _userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(7);

            var result = Assert.IsType<OkObjectResult>(await Controller().MarkAllRead());

            Assert.Equal(7, Assert.IsType<MarkAllReadResponseDTO>(result.Value).Marked);
        }

        [Fact]
        public async Task MarkAllRead_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().MarkAllRead());
        }

        /// <summary>
        /// A broadcast reaches every screen in the company, so writing one pushes immediately
        /// rather than waiting for the next fifteen-minute sweep.
        /// </summary>
        [Fact]
        public async Task Broadcast_WritesItAndPushesTheTenantAtOnce()
        {
            var created = new Notification { Id = Guid.NewGuid() };
            _service.Setup(s => s.BroadcastAsync(
                    _enterpriseId, _userId, It.IsAny<CreateBroadcastDTO>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(created);

            Assert.IsType<OkObjectResult>(await Controller().Broadcast(new CreateBroadcastDTO
            {
                Title = "Manutenção",
                Body = "Sábado das 8h às 10h."
            }));

            _group.Verify(
                g => g.SendCoreAsync("NotificationsChanged", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Broadcast_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().Broadcast(new CreateBroadcastDTO()));
        }

        [Fact]
        public async Task RemoveBroadcast_ThatIsNotThisTenants_IsNotFound()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.RemoveBroadcastAsync(id, _enterpriseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            Assert.IsType<NotFoundResult>(await Controller().RemoveBroadcast(id));
            _group.Verify(
                g => g.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task RemoveBroadcast_RetiresItAndPushes()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.RemoveBroadcastAsync(id, _enterpriseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            Assert.IsType<NoContentResult>(await Controller().RemoveBroadcast(id));
            _group.Verify(
                g => g.SendCoreAsync("NotificationsChanged", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// A push that fails must not fail the write — the message is already stored, and the
        /// client refetches on its own schedule anyway.
        /// </summary>
        [Fact]
        public async Task Broadcast_WhenTheHubIsDown_StillSucceeds()
        {
            _service.Setup(s => s.BroadcastAsync(
                    _enterpriseId, _userId, It.IsAny<CreateBroadcastDTO>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Notification { Id = Guid.NewGuid() });
            _group.Setup(g => g.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("hub is gone"));

            Assert.IsType<OkObjectResult>(await Controller().Broadcast(new CreateBroadcastDTO { Title = "T", Body = "B" }));
        }

        /// <summary>
        /// The sweep exists to push only where the firing set changed. A tenant whose alerts are
        /// steady costs one panel read and one cache get, and wakes nobody.
        /// </summary>
        [Fact]
        public async Task SweepAlerts_WhenTheAlertSetIsUnchanged_PushesNothing()
        {
            var enterpriseId = Guid.NewGuid();
            _enterprises.Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Enterprise, bool>>>()))
                .ReturnsAsync([new Enterprise { Id = enterpriseId, IsActive = true }]);
            _service.Setup(s => s.GetAlertsAsync(enterpriseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(([], "payables.overdue"));

            // Whatever the controller hashed last time, returned verbatim.
            string? stored = null;
            _cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                    It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
                .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                    (_, value, _, _) => stored = System.Text.Encoding.UTF8.GetString(value))
                .Returns(Task.CompletedTask);
            _cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => stored == null ? null : System.Text.Encoding.UTF8.GetBytes(stored));

            var first = Assert.IsType<OkObjectResult>(await Controller().SweepAlerts());
            Assert.Equal(1, Assert.IsType<NotificationSweepResultDTO>(first.Value).EnterprisesNotified);

            var second = Assert.IsType<OkObjectResult>(await Controller().SweepAlerts());
            var result = Assert.IsType<NotificationSweepResultDTO>(second.Value);

            Assert.Equal(1, result.EnterprisesScanned);
            Assert.Equal(0, result.EnterprisesNotified);
        }

        /// <summary>
        /// A null fingerprint means the panel could not be computed, which is a failure to record
        /// rather than a reason to push an empty bell.
        /// </summary>
        [Fact]
        public async Task SweepAlerts_WhenATenantsPanelCannotBeComputed_CountsItAsFailed()
        {
            var enterpriseId = Guid.NewGuid();
            _enterprises.Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Enterprise, bool>>>()))
                .ReturnsAsync([new Enterprise { Id = enterpriseId, IsActive = true }]);
            _service.Setup(s => s.GetAlertsAsync(enterpriseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(([], null));

            var result = Assert.IsType<NotificationSweepResultDTO>(
                Assert.IsType<OkObjectResult>(await Controller().SweepAlerts()).Value);

            Assert.Equal(1, result.EnterprisesFailed);
            Assert.Equal(0, result.EnterprisesNotified);
        }

        /// <summary>
        /// One tenant throwing must not end the sweep — the remaining companies still get their
        /// bell, and the failure is counted rather than swallowed.
        /// </summary>
        [Fact]
        public async Task SweepAlerts_WhenOneTenantThrows_KeepsGoing()
        {
            var bad = Guid.NewGuid();
            var good = Guid.NewGuid();
            _enterprises.Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Enterprise, bool>>>()))
                .ReturnsAsync([new Enterprise { Id = bad, IsActive = true }, new Enterprise { Id = good, IsActive = true }]);
            _service.Setup(s => s.GetAlertsAsync(bad, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("panel blew up"));
            _service.Setup(s => s.GetAlertsAsync(good, It.IsAny<CancellationToken>()))
                .ReturnsAsync(([], "stock.critical"));
            _cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[]?)null);
            _cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                    It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var result = Assert.IsType<NotificationSweepResultDTO>(
                Assert.IsType<OkObjectResult>(await Controller().SweepAlerts()).Value);

            Assert.Equal(2, result.EnterprisesScanned);
            Assert.Equal(1, result.EnterprisesFailed);
            Assert.Equal(1, result.EnterprisesNotified);
        }

        /// <summary>The sweep runs on a machine token and reads no tenant claim of its own.</summary>
        [Fact]
        public async Task SweepAlerts_WithNoEnterprises_IsStillASuccessfulRun()
        {
            _enterprises.Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Enterprise, bool>>>()))
                .ReturnsAsync([]);

            var result = Assert.IsType<NotificationSweepResultDTO>(
                Assert.IsType<OkObjectResult>(await Anonymous().SweepAlerts()).Value);

            Assert.Equal(0, result.EnterprisesScanned);
        }
    }
}
