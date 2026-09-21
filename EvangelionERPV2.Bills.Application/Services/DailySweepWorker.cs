using EvangelionERPV2.BillsModule.Application.Configs;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// The once-a-day pass over the money: the régua de cobrança and the contas fixas.
    /// </summary>
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

            try
            {
                var recurringPayableService = scope.ServiceProvider.GetRequiredService<RecurringPayableService>();
                var generated = await recurringPayableService.SweepAsync(stoppingToken);
                if (generated > 0)
                    Log.Logger.Information("Recurring payable sweep generated {Generated} bill(s).", generated);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Recurring payable sweep failed.");
            }
        }
    }
}
