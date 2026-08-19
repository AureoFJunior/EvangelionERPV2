using Microsoft.Extensions.Hosting;
using Serilog;

namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>
    /// Base for recurring background work that must happen once per slot across the whole cluster
    /// rather than once per replica. Subclasses say when the next slot is and what to do in it;
    /// this owns the waiting, the lease and the "a failed pass must not kill the host" rule.
    /// </summary>
    public abstract class LeasedScheduleWorker : BackgroundService
    {
        /// <summary>
        /// Floor for the lease, so a replica that starts seconds before a slot cannot take a lease
        /// too short to still be there when its siblings wake for the same slot.
        /// </summary>
        private static readonly TimeSpan MinimumLeaseTime = TimeSpan.FromSeconds(30);

        private readonly IDistributedLease _lease;

        protected LeasedScheduleWorker(IDistributedLease lease)
        {
            _lease = lease;
        }

        /// <summary>Key every replica of this job races for. One per job, or they block each other.</summary>
        public abstract string LeaseKey { get; }

        /// <summary>
        /// How long to wait before the next pass. Takes the clock as an argument so the schedule is
        /// testable without waiting for it.
        /// </summary>
        public abstract TimeSpan TimeUntilNextRun(DateTime utcNow);

        /// <summary>The work itself. Runs only on the replica that holds the lease.</summary>
        protected abstract Task RunPassAsync(CancellationToken stoppingToken);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Wait before the first pass: running on startup would repeat the slot on every
                    // restart and deploy.
                    await Task.Delay(TimeUntilNextRun(DateTime.UtcNow), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                // Task.Delay never returns early, so we are at or past the slot boundary and this is
                // a whole slot's worth of time — the interval the lease has to cover.
                await RunOnceUnderLeaseAsync(TimeUntilNextRun(DateTime.UtcNow), stoppingToken);
            }
        }

        /// <summary>
        /// One slot: take the lease, run the pass, keep the lease for the rest of the slot. Returns
        /// whether this replica was the one that ran. Public because it, not the timer, is the unit
        /// of behaviour worth testing.
        /// </summary>
        public async Task<bool> RunOnceUnderLeaseAsync(TimeSpan slot, CancellationToken stoppingToken)
        {
            var token = await _lease.TryAcquireAsync(LeaseKey, LeaseTimeFor(slot));
            if (token == null)
            {
                Log.Logger.Information(
                    "{Worker} skipped this slot: {LeaseKey} is held elsewhere.",
                    GetType().Name,
                    LeaseKey);

                return false;
            }

            try
            {
                await RunPassAsync(stoppingToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                // Shutting down mid-pass keeps the lease: the pass may already have had its effect,
                // and the replacement replica must not repeat it.
                throw;
            }
            catch (Exception exception)
            {
                // A failed pass must not kill the host, and must not sit on the slot either —
                // handing the lease back lets a healthy replica retry now instead of losing the slot.
                Log.Logger.Error(exception, "{Worker} pass failed.", GetType().Name);
                await _lease.ReleaseAsync(LeaseKey, token);
                return false;
            }
        }

        /// <summary>
        /// The lease is deliberately *not* released when a pass succeeds: replicas wake within
        /// milliseconds of each other, so a lease handed back the moment the work finished would go
        /// straight to the next replica and send everything twice. Holding it for most of the slot
        /// makes it the slot marker; expiring before the slot ends means the next slot is raced for
        /// again even if this replica died holding it.
        /// </summary>
        private static TimeSpan LeaseTimeFor(TimeSpan slot)
        {
            // ponytail: no renewal, so a pass that outruns 80% of its own slot can lose the lease
            // mid-flight. Renew from a timer if a pass ever gets that slow.
            var scaled = TimeSpan.FromTicks((long)(slot.Ticks * 0.8));
            return scaled < MinimumLeaseTime ? MinimumLeaseTime : scaled;
        }
    }
}
