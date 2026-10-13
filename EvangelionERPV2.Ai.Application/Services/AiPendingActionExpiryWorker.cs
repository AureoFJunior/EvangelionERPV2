using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;

namespace EvangelionERPV2.AiModule.Application.Services
{
    /// <summary>
    /// Two housekeeping jobs on one schedule and one lease: expiring proposals nobody answered, and
    /// deleting conversations past their retention window.
    /// </summary>
    public sealed class AiPendingActionExpiryWorker : LeasedScheduleWorker
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly AiSettings _settings;

        public AiPendingActionExpiryWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<AiSettings> settings,
            IDistributedLease lease)
            : base(lease)
        {
            _scopeFactory = scopeFactory;
            _settings = settings?.Value ?? new AiSettings();
        }

        public override string LeaseKey => "eva:lease:ai-pending-action-expiry";

        public override TimeSpan TimeUntilNextRun(DateTime utcNow)
            => TimeSpan.FromMinutes(Math.Max(1, _settings.PendingActionSweepIntervalMinutes));

        protected override async Task RunPassAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IAiPendingActionService>();

            var expired = await service.SweepExpiredAsync(stoppingToken);
            if (expired > 0)
                Log.Logger.Information("AI expiry sweep marked {Expired} pending action(s) as expired.", expired);

            var retention = scope.ServiceProvider.GetRequiredService<IAiRetentionService>();
            var purged = await retention.PurgeAsync(stoppingToken);
            if (purged > 0)
                Log.Logger.Information("AI retention sweep deleted {Purged} conversation(s) past the window.", purged);
        }
    }
}
