using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;

namespace EvangelionERPV2.NFeModule.Application.Services
{
    /// <summary>
    /// Periodically runs <see cref="NFeReconciliationService"/> on one replica at a time. Thin on
    /// purpose: everything worth testing lives in the service, this only owns the schedule and the scope.
    /// </summary>
    public sealed class NFeReconciliationWorker : LeasedScheduleWorker
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly NFeSettings _settings;

        public NFeReconciliationWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<NFeSettings> settings,
            IDistributedLease lease)
            : base(lease)
        {
            _scopeFactory = scopeFactory;
            _settings = settings?.Value ?? new NFeSettings();
        }

        public override string LeaseKey => "eva:lease:nfe-reconciliation";

        public override TimeSpan TimeUntilNextRun(DateTime utcNow)
            => TimeSpan.FromMinutes(Math.Max(1, _settings.ReconciliationIntervalMinutes));

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_settings.Enabled)
            {
                Log.Logger.Information("NFe reconciliation sweeper not started: issuance is disabled.");
                return Task.CompletedTask;
            }

            return base.ExecuteAsync(stoppingToken);
        }

        protected override async Task RunPassAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var reconciliationService = scope.ServiceProvider.GetRequiredService<NFeReconciliationService>();

            var resolved = await reconciliationService.ReconcileAsync(stoppingToken);
            if (resolved > 0)
                Log.Logger.Information("NFe reconciliation sweep resolved {Resolved} document(s).", resolved);
        }
    }
}
