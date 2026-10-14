using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.NotificationModule.Application.Services;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.NotificationModule.Test.Notification
{
    /// <summary>
    /// The feed decides what a person sees in their bell and whether the badge ever reaches zero.
    /// Every case here is one someone would otherwise report: a retired message still showing, an
    /// expiry that never takes effect, one person's reading clearing everyone else's badge.
    /// </summary>
    public class NotificationServiceTests
    {
        private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid Reader = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid Colleague = Guid.Parse("44444444-4444-4444-4444-444444444444");

        [Fact]
        public async Task TheFeed_CarriesBroadcastsAndAlertsTogether()
        {
            var world = new World();
            world.AddBroadcast("Parada para manutenção");
            world.Panel.Payables.OverdueCount = 1;

            var feed = await world.Service.GetFeedAsync(Tenant, Reader);

            Assert.Equal(2, feed.Items.Count);
            Assert.Equal(1, feed.UnreadBroadcastCount);
            Assert.Equal(1, feed.ActiveAlertCount);
        }

        [Fact]
        public async Task AnAlert_NeverCountsAsAnUnreadMessage()
        {
            var world = new World();
            world.Panel.Payables.OverdueCount = 3;
            world.Panel.Stock.CriticalCount = 2;

            var feed = await world.Service.GetFeedAsync(Tenant, Reader);

            Assert.Equal(2, feed.ActiveAlertCount);
            Assert.Equal(0, feed.UnreadBroadcastCount);
        }

        [Fact]
        public async Task AnExpiredBroadcast_IsGone_AndOneExpiringTomorrowIsNot()
        {
            var world = new World();
            world.AddBroadcast("Ontem", expiresAt: DateTime.UtcNow.AddMinutes(-1));
            world.AddBroadcast("Amanhã", expiresAt: DateTime.UtcNow.AddDays(1));

            var feed = await world.Service.GetFeedAsync(Tenant, Reader);

            Assert.Equal("Amanhã", Assert.Single(feed.Items).Title);
        }

        [Fact]
        public async Task ARetiredBroadcast_IsGone()
        {
            var world = new World();
            var id = world.AddBroadcast("Aviso errado");

            Assert.True(await world.Service.RemoveBroadcastAsync(id, Tenant));

            Assert.Empty((await world.Service.GetFeedAsync(Tenant, Reader)).Items);
        }

        [Fact]
        public async Task AnotherTenantsBroadcast_IsNotRetired_AndNotFound()
        {
            var world = new World();
            var id = world.AddBroadcast("Deles", enterpriseId: Other);

            Assert.False(await world.Service.RemoveBroadcastAsync(id, Tenant));
            Assert.True(world.Broadcasts.Single().IsActive);
        }

        [Fact]
        public async Task ReadingTwice_WritesOneRow()
        {
            var world = new World();
            var id = world.AddBroadcast("Leia isto");

            await world.Service.MarkReadAsync(id, Tenant, Reader);
            await world.Service.MarkReadAsync(id, Tenant, Reader);

            Assert.Single(world.Reads);
            Assert.Equal(0, (await world.Service.GetFeedAsync(Tenant, Reader)).UnreadBroadcastCount);
        }

        [Fact]
        public async Task OneUserReading_LeavesTheirColleaguesBadgeAlone()
        {
            var world = new World();
            var id = world.AddBroadcast("Leia isto");

            await world.Service.MarkReadAsync(id, Tenant, Reader);

            Assert.Equal(1, (await world.Service.GetFeedAsync(Tenant, Colleague)).UnreadBroadcastCount);
        }

        [Fact]
        public async Task MarkingAnotherTenantsBroadcastRead_WritesNothing()
        {
            var world = new World();
            var id = world.AddBroadcast("Deles", enterpriseId: Other);

            await world.Service.MarkReadAsync(id, Tenant, Reader);

            Assert.Empty(world.Reads);
        }

        [Fact]
        public async Task MarkAllRead_ClearsTheBadgeInOneCommit()
        {
            var world = new World();
            world.AddBroadcast("Um");
            world.AddBroadcast("Dois");

            Assert.Equal(2, await world.Service.MarkAllReadAsync(Tenant, Reader));
            Assert.Equal(0, (await world.Service.GetFeedAsync(Tenant, Reader)).UnreadBroadcastCount);

            Assert.Equal(0, await world.Service.MarkAllReadAsync(Tenant, Reader));
            Assert.Equal(2, world.Reads.Count);
        }

        [Fact]
        public async Task MarkAllRead_OnAnEmptyFeed_WritesNothing()
        {
            var world = new World();

            Assert.Equal(0, await world.Service.MarkAllReadAsync(Tenant, Reader));
            Assert.Empty(world.Reads);
            world.NotificationReads.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task APanelThatCannotBeComputed_CostsTheAlerts_NeverTheBroadcasts()
        {
            var world = new World();
            world.AddBroadcast("Ainda aqui");
            world.PanelService
                .Setup(x => x.GetTodayAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("panel down"));

            var feed = await world.Service.GetFeedAsync(Tenant, Reader);

            Assert.Equal("Ainda aqui", Assert.Single(feed.Items).Title);
            Assert.Equal(0, feed.ActiveAlertCount);
        }

        [Fact]
        public async Task APanelThatCannotBeComputed_ReadsAsUnknown_NotAsAlertsCleared()
        {
            var world = new World();
            world.PanelService
                .Setup(x => x.GetTodayAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("panel down"));

            var (alerts, fingerprint) = await world.Service.GetAlertsAsync(Tenant);

            Assert.Empty(alerts);
            Assert.Null(fingerprint);
        }

        [Fact]
        public async Task AQuietTenant_HasAFingerprint_JustAnEmptyOne()
        {
            var world = new World();

            var (alerts, fingerprint) = await world.Service.GetAlertsAsync(Tenant);

            Assert.Empty(alerts);
            Assert.Equal(string.Empty, fingerprint);
        }

        [Fact]
        public async Task AnEmptyTableThrowing_ReadsAsAnEmptyFeed()
        {
            var world = new World();
            world.Notifications
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Shared.Entities.Notification, bool>>>()))
                .ThrowsAsync(new NotFoundDatabaseException("no rows"));

            var feed = await world.Service.GetFeedAsync(Tenant, Reader);

            Assert.Empty(feed.Items);
        }

        [Fact]
        public void ThePermissionGate_IsNeverSerialised()
        {
            var property = typeof(NotificationItemDTO).GetProperty(nameof(NotificationItemDTO.RequiredPermission))!;

            Assert.NotNull(property.GetCustomAttribute<JsonIgnoreAttribute>());
        }

        /// <summary>Repositories backed by lists, so the service's own predicates are what is tested.</summary>
        private sealed class World
        {
            public List<Shared.Entities.Notification> Broadcasts { get; } = [];
            public List<NotificationRead> Reads { get; } = [];
            public DailyPanelDTO Panel { get; } = new();

            public Mock<IRepository<Shared.Entities.Notification>> Notifications { get; } = new();
            public Mock<IRepository<NotificationRead>> NotificationReads { get; } = new();
            public Mock<IDailyPanelService> PanelService { get; } = new();

            public NotificationService Service { get; }

            public World()
            {
                Notifications
                    .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Shared.Entities.Notification, bool>>>()))
                    .ReturnsAsync((Expression<Func<Shared.Entities.Notification, bool>> predicate) =>
                        Broadcasts.Where(predicate.Compile()).ToList());

                Notifications
                    .Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
                    .ReturnsAsync((Guid id) => Broadcasts.SingleOrDefault(x => x.Id == id)!);

                Notifications
                    .Setup(x => x.CreateAsync(It.IsAny<Shared.Entities.Notification>()))
                    .ReturnsAsync((Shared.Entities.Notification entity) =>
                    {
                        Broadcasts.Add(entity);
                        return entity;
                    });

                NotificationReads
                    .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<NotificationRead, bool>>>()))
                    .ReturnsAsync((Expression<Func<NotificationRead, bool>> predicate) =>
                        Reads.Where(predicate.Compile()).ToList());

                NotificationReads
                    .Setup(x => x.CreateAsync(It.IsAny<NotificationRead>()))
                    .ReturnsAsync((NotificationRead entity) =>
                    {
                        Reads.Add(entity);
                        return entity;
                    });

                NotificationReads
                    .Setup(x => x.CreateRangeAsync(It.IsAny<IEnumerable<NotificationRead>>()))
                    .ReturnsAsync((IEnumerable<NotificationRead> entities) =>
                    {
                        var created = entities.ToList();
                        Reads.AddRange(created);
                        return created;
                    });

                PanelService
                    .Setup(x => x.GetTodayAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => Panel);

                Service = new NotificationService(Notifications.Object, NotificationReads.Object, PanelService.Object);
            }

            public Guid AddBroadcast(string title, Guid? enterpriseId = null, DateTime? expiresAt = null)
            {
                var broadcast = new Shared.Entities.Notification
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId ?? Tenant,
                    Title = title,
                    Body = title,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expiresAt,
                };

                Broadcasts.Add(broadcast);

                return broadcast.Id;
            }
        }
    }
}
