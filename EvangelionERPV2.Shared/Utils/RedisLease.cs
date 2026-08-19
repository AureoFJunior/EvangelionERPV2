using Microsoft.Extensions.Configuration;
using Serilog;
using StackExchange.Redis;

namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>
    /// A cluster-wide "this slot is mine" marker, so scheduled work runs once per slot instead of
    /// once per replica. Kept behind an interface only because the scheduled workers have to be
    /// testable without a Redis server.
    /// </summary>
    public interface IDistributedLease
    {
        /// <summary>
        /// Returns an owner token when this caller took the lease, or <c>null</c> when someone else
        /// holds it — or when the lease could not be evaluated at all.
        /// </summary>
        Task<string?> TryAcquireAsync(string key, TimeSpan timeToLive);

        /// <summary>Gives the lease up early. A no-op unless <paramref name="token"/> still owns it.</summary>
        Task ReleaseAsync(string key, string token);
    }

    /// <summary>
    /// <see cref="IDistributedLease"/> over the Redis this solution already runs for caching:
    /// <c>SET key token NX PX ttl</c> to take it, compare-and-delete to give it back.
    /// </summary>
    public sealed class RedisLease : IDistributedLease
    {
        /// <summary>
        /// Deleting the key unconditionally would drop the lease a *different* replica took after
        /// ours expired mid-pass, putting two replicas in the critical section at once — strictly
        /// worse than never releasing. The compare and the delete have to be one step, hence a script.
        /// </summary>
        private const string ReleaseIfOwnerScript =
            "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

        private readonly Lazy<IConnectionMultiplexer> _connection;

        public RedisLease(AWSKMSKeyProvider kmsProvider, IConfiguration configuration)
        {
            // Resolved on first use rather than at startup: the connection string is a Secrets
            // Manager reference, and a host that cannot boot because Secrets Manager is slow is a
            // worse outage than a sweep that skips a tick.
            _connection = new Lazy<IConnectionMultiplexer>(() =>
            {
                var options = ConfigurationOptions.Parse(
                    kmsProvider.GetKMSKey(configuration.GetSection("RedisSettings")["ConnectionString"] ?? string.Empty));

                // The multiplexer reconnects on its own; without this, a Redis that happens to be
                // down on first use would poison the Lazy for the life of the process.
                options.AbortOnConnectFail = false;
                return ConnectionMultiplexer.Connect(options);
            });
        }

        public async Task<string?> TryAcquireAsync(string key, TimeSpan timeToLive)
        {
            var token = Guid.NewGuid().ToString("N");

            try
            {
                return await _connection.Value.GetDatabase().StringSetAsync(key, token, timeToLive, When.NotExists)
                    ? token
                    : null;
            }
            catch (Exception exception)
            {
                // Redis unreachable => stand down, do not run. Failing open would let every replica
                // run at once, which is exactly the double-send this lease exists to stop, and it
                // would do it during an incident when nobody is reading the logs. Every caller is a
                // catch-up sweep or a recurring broadcast, so a skipped slot costs one delayed cycle;
                // failing open costs a duplicate customer email, which cannot be taken back.
                Log.Logger.Warning(
                    "Lease {LeaseKey} could not be taken, standing down this slot. ErrorType={ErrorType}",
                    key,
                    exception.GetType().Name);

                return null;
            }
        }

        public async Task ReleaseAsync(string key, string token)
        {
            try
            {
                await _connection.Value.GetDatabase().ScriptEvaluateAsync(
                    ReleaseIfOwnerScript,
                    new RedisKey[] { key },
                    new RedisValue[] { token });
            }
            catch (Exception exception)
            {
                // Nothing to do but let the TTL expire, which is the whole point of having one.
                Log.Logger.Warning(
                    "Lease {LeaseKey} could not be released, letting it expire. ErrorType={ErrorType}",
                    key,
                    exception.GetType().Name);
            }
        }
    }
}
