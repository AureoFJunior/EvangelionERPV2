using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace EvangelionERPV2.Web.Security
{
    /// <summary>
    /// Counts failed attempts against a key inside a rolling window and reports when the key has
    /// had too many.
    /// </summary>
    /// <remarks>
    /// The shape is <c>UserController</c>'s reset-password limiter: a fixed window, a cap on
    /// tracked keys so a spray cannot grow the dictionary without bound, and a periodic sweep of
    /// expired entries. It lives here as a type because the till's login needs two of these — one
    /// per terminal, one per terminal-and-operator — and two hand-copied sets of the same fields
    /// would be two places to get the window wrong.
    ///
    /// In-process and per-instance, so a horizontally scaled deployment limits per replica. That is
    /// the same ceiling the reset-password limiter has; a shared counter needs a shared store.
    /// </remarks>
    public sealed class FailedAttemptLimiter
    {
        private readonly ConcurrentDictionary<string, Entry> _entries = new();
        private readonly object _sync = new();
        private readonly TimeSpan _window;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(1);
        private readonly int _maxFailedAttempts;
        private readonly int _maxEntries;
        private long _lastCleanupTicks;

        public FailedAttemptLimiter(TimeSpan window, int maxFailedAttempts, int maxEntries = 5000)
        {
            _window = window;
            _maxFailedAttempts = maxFailedAttempts;
            _maxEntries = maxEntries;
        }

        /// <summary>
        /// A fixed-length, non-reversible key. What gets counted is a username or a terminal id, and
        /// neither belongs in a process-lifetime dictionary in the clear.
        /// </summary>
        public static string StableKey(string source) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source ?? string.Empty))).ToLowerInvariant();

        public bool IsLimited(string key)
        {
            var now = DateTime.UtcNow;
            TryCleanupExpired(now);

            if (!_entries.TryGetValue(key, out var entry))
                return false;

            if (now - entry.WindowStartedAt >= _window)
            {
                _entries.TryRemove(key, out _);
                return false;
            }

            return entry.FailedAttempts >= _maxFailedAttempts;
        }

        public void RegisterFailure(string key)
        {
            var now = DateTime.UtcNow;
            TryCleanupExpired(now);

            lock (_sync)
            {
                if (!_entries.ContainsKey(key) && _entries.Count >= _maxEntries)
                    TryRemoveOldest();

                // Still full: every tracked key is younger than this one, so drop this attempt
                // rather than evict a counter that is actively holding an attacker back.
                if (!_entries.ContainsKey(key) && _entries.Count >= _maxEntries)
                    return;

                _entries.AddOrUpdate(
                    key,
                    _ => new Entry(now, 1),
                    (_, existing) => now - existing.WindowStartedAt >= _window
                        ? new Entry(now, 1)
                        : existing with { FailedAttempts = existing.FailedAttempts + 1 });
            }
        }

        public void Clear(string key) => _entries.TryRemove(key, out _);

        private void TryRemoveOldest()
        {
            string? oldestKey = null;
            var oldestWindowStartedAt = DateTime.MaxValue;

            foreach (var entry in _entries)
            {
                if (entry.Value.WindowStartedAt < oldestWindowStartedAt)
                {
                    oldestWindowStartedAt = entry.Value.WindowStartedAt;
                    oldestKey = entry.Key;
                }
            }

            if (!string.IsNullOrWhiteSpace(oldestKey))
                _entries.TryRemove(oldestKey, out _);
        }

        private void TryCleanupExpired(DateTime now)
        {
            var nowTicks = now.Ticks;
            var lastCleanupTicks = Interlocked.Read(ref _lastCleanupTicks);
            if (lastCleanupTicks != 0 && nowTicks - lastCleanupTicks < _cleanupInterval.Ticks)
                return;

            if (Interlocked.CompareExchange(ref _lastCleanupTicks, nowTicks, lastCleanupTicks) != lastCleanupTicks)
                return;

            foreach (var entry in _entries)
            {
                if (now - entry.Value.WindowStartedAt >= _window)
                    _entries.TryRemove(entry.Key, out _);
            }
        }

        private sealed record Entry(DateTime WindowStartedAt, int FailedAttempts);
    }
}
