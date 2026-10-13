using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;

namespace EvangelionERPV2.NFeModule.Application.Services
{
    /// <summary>
    /// Runs <see cref="NFeContingencyService"/> on one replica at a time. Thin on purpose:
    /// everything worth testing lives in the service.
    /// </summary>
    public sealed class NFeContingencyWorker : LeasedScheduleWorker
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly NFeSettings _settings;

        public NFeContingencyWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<NFeSettings> settings,
            IDistributedLease lease)
            : base(lease)
        {
            _scopeFactory = scopeFactory;
            _settings = settings?.Value ?? new NFeSettings();
        }

        public override string LeaseKey => "eva:lease:nfe-contingency";

        public override TimeSpan TimeUntilNextRun(DateTime utcNow)
            => TimeSpan.FromMinutes(Math.Max(1, _settings.ContingencyIntervalMinutes));

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_settings.Enabled)
            {
                Log.Logger.Information("NFe contingency sweeper not started: issuance is disabled.");
                return Task.CompletedTask;
            }

            return base.ExecuteAsync(stoppingToken);
        }

        protected override async Task RunPassAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var contingencyService = scope.ServiceProvider.GetRequiredService<NFeContingencyService>();

            var transmitted = await contingencyService.TransmitPendingAsync(stoppingToken);
            if (transmitted > 0)
                Log.Logger.Information("Contingency sweep transmitted {Transmitted} note(s).", transmitted);
        }
    }
}
