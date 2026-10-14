using EvangelionERPV2.EmployeeModule.Application.Configs;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;

namespace EvangelionERPV2.EmployeeModule.Application.Services
{
    /// <summary>
    /// Periodically runs <see cref="EmployeeVacationLapseService"/> on one replica at a time. Thin
    /// on purpose: everything worth testing lives in the service, this only owns the schedule and
    /// the scope.
    /// </summary>
    public sealed class EmployeeVacationLapseWorker : LeasedScheduleWorker
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly EmployeeVacationSettings _settings;

        public EmployeeVacationLapseWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<EmployeeVacationSettings> settings,
            IDistributedLease lease)
            : base(lease)
        {
            _scopeFactory = scopeFactory;
            _settings = settings?.Value ?? new EmployeeVacationSettings();
        }

        public override string LeaseKey => "eva:lease:vacation-lapse";

        public override TimeSpan TimeUntilNextRun(DateTime utcNow)
            => TimeSpan.FromMinutes(Math.Max(1, _settings.LapseSweepIntervalMinutes));

        protected override async Task RunPassAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var lapseService = scope.ServiceProvider.GetRequiredService<EmployeeVacationLapseService>();

            var lapsed = await lapseService.SweepLapsedAsync(stoppingToken);
            if (lapsed > 0)
                Log.Logger.Warning("Vacation lapse sweep marked {Lapsed} accrual(s) as owed in dobro.", lapsed);
        }
    }
}
