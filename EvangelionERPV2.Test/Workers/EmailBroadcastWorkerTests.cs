using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Worker.EmailModule.EmailWorker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EvangelionERPV2.Test.Workers
{
    /// <summary>
    /// Covers the schedule and the lease, never SMTP. A pass is driven far enough to prove it ran
    /// (it reaches Secrets Manager for the self-API credentials) and no further: the stub secret
    /// does not parse into a credential, so the worker logs and returns before any HTTP call.
    /// </summary>
    public class EmailBroadcastWorkerTests
    {
        #region Schedule

        [Theory]
        [InlineData("2026-08-19T09:30:00Z", "2026-08-24T08:00:00Z")]
        [InlineData("2026-08-24T07:59:59Z", "2026-08-24T08:00:00Z")]
        [InlineData("2026-08-24T08:00:00Z", "2026-08-31T08:00:00Z")]
        [InlineData("2026-08-24T08:00:01Z", "2026-08-31T08:00:00Z")]
        public void StockWorker_TimeUntilNextRun_TargetsTheComingMondaySlot(string utcNow, string expectedSlot)
        {
            var now = DateTime.Parse(utcNow).ToUniversalTime();
            var worker = new EmailStockSenderWorker(ScopeFactoryFor(Mock.Of<IAmazonSecretsManager>(), "unused"), EmptyConfiguration(), Mock.Of<IDistributedLease>());

            Assert.Equal(DateTime.Parse(expectedSlot).ToUniversalTime(), now + worker.TimeUntilNextRun(now));
        }

        [Theory]
        [InlineData("2026-08-19T09:30:00Z", "2026-08-20T08:00:00Z")]
        [InlineData("2026-08-19T07:59:59Z", "2026-08-19T08:00:00Z")]
        [InlineData("2026-08-31T08:00:00Z", "2026-09-01T08:00:00Z")]
        public void MonthlyWorker_TimeUntilNextRun_TargetsTheNextDailySlot(string utcNow, string expectedSlot)
        {
            var now = DateTime.Parse(utcNow).ToUniversalTime();
            var worker = new EmailMonthlyOrderSenderWorker(ScopeFactoryFor(Mock.Of<IAmazonSecretsManager>(), "unused"), EmptyConfiguration(), Mock.Of<IDistributedLease>());

            Assert.Equal(DateTime.Parse(expectedSlot).ToUniversalTime(), now + worker.TimeUntilNextRun(now));
        }

        [Fact]
        public void Workers_UseDistinctLeaseKeys()
        {
            var stock = new EmailStockSenderWorker(ScopeFactoryFor(Mock.Of<IAmazonSecretsManager>(), "unused"), EmptyConfiguration(), Mock.Of<IDistributedLease>());
            var monthly = new EmailMonthlyOrderSenderWorker(ScopeFactoryFor(Mock.Of<IAmazonSecretsManager>(), "unused"), EmptyConfiguration(), Mock.Of<IDistributedLease>());

            Assert.NotEqual(stock.LeaseKey, monthly.LeaseKey);
        }

        #endregion

        #region Lease

        [Fact]
        public async Task StockWorker_RunsThePass_WhenItTakesTheLease()
        {
            var secretName = UniqueSecretName();
            var secretsManager = StubSecretsManager();
            var lease = new Mock<IDistributedLease>();
            lease.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync("owner-token");

            var worker = new EmailStockSenderWorker(
                ScopeFactoryFor(secretsManager.Object, secretName),
                ConfigurationFor(secretName),
                lease.Object);

            var ran = await worker.RunOnceUnderLeaseAsync(TimeSpan.FromDays(7), CancellationToken.None);

            Assert.True(ran);
            secretsManager.Verify(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()), Times.Once);

            lease.Verify(x => x.ReleaseAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task StockWorker_SkipsThePass_WhenAnotherReplicaHoldsTheLease()
        {
            var secretName = UniqueSecretName();
            var secretsManager = StubSecretsManager();
            var lease = new Mock<IDistributedLease>();
            lease.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync((string?)null);

            var worker = new EmailStockSenderWorker(
                ScopeFactoryFor(secretsManager.Object, secretName),
                ConfigurationFor(secretName),
                lease.Object);

            var ran = await worker.RunOnceUnderLeaseAsync(TimeSpan.FromDays(7), CancellationToken.None);

            Assert.False(ran);
            secretsManager.Verify(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task MonthlyWorker_SkipsThePass_WhenAnotherReplicaHoldsTheLease()
        {
            var secretName = UniqueSecretName();
            var secretsManager = StubSecretsManager();
            var lease = new Mock<IDistributedLease>();
            lease.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync((string?)null);

            var worker = new EmailMonthlyOrderSenderWorker(
                ScopeFactoryFor(secretsManager.Object, secretName),
                ConfigurationFor(secretName),
                lease.Object);

            Assert.False(await worker.RunOnceUnderLeaseAsync(TimeSpan.FromDays(1), CancellationToken.None));
            secretsManager.Verify(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task StockWorker_StandsDown_WhenRedisIsUnavailable()
        {
            var secretName = UniqueSecretName();
            var secretsManager = StubSecretsManager();

            var lease = new Mock<IDistributedLease>();
            lease.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync((string?)null);

            var worker = new EmailStockSenderWorker(
                ScopeFactoryFor(secretsManager.Object, secretName),
                ConfigurationFor(secretName),
                lease.Object);

            Assert.False(await worker.RunOnceUnderLeaseAsync(TimeSpan.FromDays(7), CancellationToken.None));
            secretsManager.Verify(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task TwoReplicas_OnTheSameSlot_SendOnlyOnce()
        {
            var lease = new InMemoryLease();

            var firstSecretName = UniqueSecretName();
            var firstSecrets = StubSecretsManager();
            var first = new EmailStockSenderWorker(ScopeFactoryFor(firstSecrets.Object, firstSecretName), ConfigurationFor(firstSecretName), lease);

            var secondSecretName = UniqueSecretName();
            var secondSecrets = StubSecretsManager();
            var second = new EmailStockSenderWorker(ScopeFactoryFor(secondSecrets.Object, secondSecretName), ConfigurationFor(secondSecretName), lease);

            var firstRan = await first.RunOnceUnderLeaseAsync(TimeSpan.FromDays(7), CancellationToken.None);
            var secondRan = await second.RunOnceUnderLeaseAsync(TimeSpan.FromDays(7), CancellationToken.None);

            Assert.True(firstRan);
            Assert.False(secondRan);

            firstSecrets.Verify(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            secondSecrets.Verify(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task LeaseIsHeldForMostOfTheSlot_ButExpiresBeforeTheNextOne()
        {
            var secretName = UniqueSecretName();
            var lease = new Mock<IDistributedLease>();
            var capturedTimeToLive = TimeSpan.Zero;
            lease.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .Callback<string, TimeSpan>((_, timeToLive) => capturedTimeToLive = timeToLive)
                .ReturnsAsync("owner-token");

            var worker = new EmailStockSenderWorker(
                ScopeFactoryFor(StubSecretsManager().Object, secretName),
                ConfigurationFor(secretName),
                lease.Object);

            var slot = TimeSpan.FromDays(7);
            await worker.RunOnceUnderLeaseAsync(slot, CancellationToken.None);

            Assert.True(capturedTimeToLive > TimeSpan.FromDays(1), "the lease must outlast any plausible skew between replicas");
            Assert.True(capturedTimeToLive < slot, "the lease must be gone before the next slot, or a dead holder blocks it forever");
        }

        [Fact]
        public async Task FailedPass_HandsTheLeaseBack_SoAHealthyReplicaCanRetryTheSlot()
        {
            var lease = new InMemoryLease();
            var worker = new ThrowingWorker(lease);

            Assert.False(await worker.RunOnceUnderLeaseAsync(TimeSpan.FromMinutes(5), CancellationToken.None));

            Assert.NotNull(await lease.TryAcquireAsync(worker.LeaseKey, TimeSpan.FromMinutes(5)));
        }

        #endregion

        #region Helpers

        private static string UniqueSecretName()
        {
            return $"evatest-{Guid.NewGuid():N}";
        }

        private static Mock<IAmazonSecretsManager> StubSecretsManager()
        {
            var secretsManager = new Mock<IAmazonSecretsManager>();
            secretsManager
                .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                // Deliberately not parseable as a credential: the pass gets far enough to prove it
                // ran, then stops before it would log in or send anything.
                .ReturnsAsync(new GetSecretValueResponse { SecretString = "not-a-credential" });

            return secretsManager;
        }

        private static IConfiguration EmptyConfiguration()
            => new ConfigurationBuilder().Build();

        private static IConfiguration ConfigurationFor(string secretName)
            => new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["SelfAPIAuth"] = secretName })
                .Build();

        private static IServiceScopeFactory ScopeFactoryFor(IAmazonSecretsManager secretsManager, string secretName)
        {
            var services = new ServiceCollection();
            services.AddSingleton(secretsManager);
            services.AddSingleton<IConfiguration>(ConfigurationFor(secretName));
            services.AddSingleton<AWSKMSKeyProvider>();

            return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        }

        private sealed class InMemoryLease : IDistributedLease
        {
            private readonly Dictionary<string, string> _held = new(StringComparer.Ordinal);

            public Task<string?> TryAcquireAsync(string key, TimeSpan timeToLive)
            {
                lock (_held)
                {
                    if (_held.ContainsKey(key))
                        return Task.FromResult<string?>(null);

                    var token = Guid.NewGuid().ToString("N");
                    _held[key] = token;
                    return Task.FromResult<string?>(token);
                }
            }

            public Task ReleaseAsync(string key, string token)
            {
                lock (_held)
                {
                    if (_held.TryGetValue(key, out var held) && held == token)
                        _held.Remove(key);
                }

                return Task.CompletedTask;
            }
        }

        private sealed class ThrowingWorker : LeasedScheduleWorker
        {
            public ThrowingWorker(IDistributedLease lease) : base(lease) { }

            public override string LeaseKey => "eva:lease:test-throwing";

            public override TimeSpan TimeUntilNextRun(DateTime utcNow) => TimeSpan.FromMinutes(5);

            protected override Task RunPassAsync(CancellationToken stoppingToken)
                => throw new InvalidOperationException("pass blew up");
        }

        #endregion
    }
}
