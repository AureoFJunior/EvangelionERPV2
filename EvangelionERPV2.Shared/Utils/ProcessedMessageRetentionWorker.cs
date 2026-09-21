using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>
    /// Deletes idempotency rows older than the retention window.
    /// </summary>
    public sealed class ProcessedMessageRetentionWorker : LeasedScheduleWorker
    {
        /// <summary>Comfortably past the 30-day dead-letter TTL, which is the real bound.</summary>
        public static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(45);

        private const int DeleteBatchSize = 5_000;

        private readonly IServiceScopeFactory _scopeFactory;

        public ProcessedMessageRetentionWorker(IServiceScopeFactory scopeFactory, IDistributedLease lease)
            : base(lease)
        {
            _scopeFactory = scopeFactory;
        }

        public override string LeaseKey => "eva:lease:processed-message-retention";

        public override TimeSpan TimeUntilNextRun(DateTime utcNow) => TimeSpan.FromHours(6);

        protected override async Task RunPassAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var cutoff = DateTime.UtcNow - RetentionWindow;
            var deleted = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                var batch = await context.ProcessedMessage
                    .Where(processed => processed.ProcessedAt < cutoff)
                    .OrderBy(processed => processed.ProcessedAt)
                    .Take(DeleteBatchSize)
                    .ExecuteDeleteAsync(stoppingToken);

                deleted += batch;
                if (batch < DeleteBatchSize)
                    break;
            }

            if (deleted > 0)
                Log.Logger.Information("Processed-message retention deleted {Deleted} row(s) older than {Cutoff:u}.", deleted, cutoff);
        }
    }
}
