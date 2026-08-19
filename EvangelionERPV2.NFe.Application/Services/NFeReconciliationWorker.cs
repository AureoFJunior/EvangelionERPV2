using EvangelionERPV2.NFeModule.Application.Configs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace EvangelionERPV2.NFeModule.Application.Services
{
    /// <summary>
    /// Periodically runs <see cref="NFeReconciliationService"/>. Thin on purpose: everything worth
    /// testing lives in the service, this only owns the timer and the scope.
    /// </summary>
    public sealed class NFeReconciliationWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly NFeSettings _settings;

        public NFeReconciliationWorker(IServiceScopeFactory scopeFactory, IOptions<NFeSettings> settings)
        {
            _scopeFactory = scopeFactory;
            _settings = settings?.Value ?? new NFeSettings();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_settings.Enabled)
            {
                Log.Logger.Information("NFe reconciliation sweeper not started: issuance is disabled.");
                return;
            }

            // ponytail: every instance sweeps. Consulting SEFAZ is a read, and the adopt-outcome
            // write is idempotent, so duplicate work is wasted rather than wrong. Take a lease
            // (a claimed-by/claimed-until pair on the row) if the instance count makes it matter.
            using var timer = new PeriodicTimer(
                TimeSpan.FromMinutes(Math.Max(1, _settings.ReconciliationIntervalMinutes)));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var reconciliationService = scope.ServiceProvider.GetRequiredService<NFeReconciliationService>();

                    var resolved = await reconciliationService.ReconcileAsync(stoppingToken);
                    if (resolved > 0)
                        Log.Logger.Information("NFe reconciliation sweep resolved {Resolved} document(s).", resolved);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // A failed sweep must not kill the host: the next tick retries, and the
                    // documents are still Pending until something resolves them.
                    Log.Logger.Error(exception, "NFe reconciliation sweep failed.");
                }
            }
        }
    }
}
