using EvangelionERPV2.BillsModule.Application.Configs;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// The once-a-day pass over the money: currently the régua de cobrança, and the place the
    /// recurring-payables sweep will hang off.
    /// </summary>
    /// <remarks>
    /// One worker and one lease for the whole daily slot, not one per job. Two workers waking at
    /// the same hour would be two Redis keys racing for the same wall-clock moment, and each new
    /// daily job would add another. Each job gets its own try/catch instead, so a failure in one
    /// does not cost the others their slot.
    /// </remarks>
    public sealed class DailySweepWorker : LeasedScheduleWorker
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DunningSettings _settings;

        public DailySweepWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<DunningSettings> settings,
            IDistributedLease lease)
            : base(lease)
        {
            _scopeFactory = scopeFactory;
            _settings = settings?.Value ?? new DunningSettings();
        }

        public override string LeaseKey => "eva:lease:daily-sweep";

        public override TimeSpan TimeUntilNextRun(DateTime utcNow)
            => BroadcastSchedule.TimeUntilNextDaily(utcNow, Math.Clamp(_settings.SweepHourUtc, 0, 23));

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_settings.Enabled)
            {
                Log.Logger.Information("Daily sweep not started: dunning is disabled.");
                return Task.CompletedTask;
            }

            return base.ExecuteAsync(stoppingToken);
        }

        protected override async Task RunPassAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            try
            {
                var dunningService = scope.ServiceProvider.GetRequiredService<DunningService>();
                var sent = await dunningService.SweepAsync(stoppingToken);
                if (sent > 0)
                    Log.Logger.Information("Dunning sweep sent {Sent} reminder(s).", sent);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Dunning sweep failed.");
            }
        }
    }
}
