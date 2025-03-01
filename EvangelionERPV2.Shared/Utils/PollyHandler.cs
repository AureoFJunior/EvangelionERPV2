using Polly;
using Polly.Timeout;
using Polly.Wrap;
using Serilog;

namespace EvangelionERPV2.Shared.Utils
{
    public class PollyHandler
    {
        private readonly int _retryCount;
        private readonly TimeSpan _retryDelay;
        private readonly TimeSpan _timeoutDuration;
        private readonly TimeSpan _circuitBreakerDuration;
        private readonly int _circuitBreakerFailures;

        public PolicyWrap SyncPolicyWrap { get; private set; }
        public AsyncPolicyWrap AsyncPolicyWrap { get; private set; }

        public PollyHandler(int retryCount = 3, int retryDelaySeconds = 1, int timeoutSeconds = 5, int circuitBreakerFailures = 2, int circuitBreakerDurationSeconds = 30)
        {
            _retryCount = retryCount;
            _retryDelay = TimeSpan.FromSeconds(retryDelaySeconds);
            _timeoutDuration = TimeSpan.FromSeconds(timeoutSeconds);
            _circuitBreakerDuration = TimeSpan.FromSeconds(circuitBreakerDurationSeconds);
            _circuitBreakerFailures = circuitBreakerFailures;

            InitializePolicies();
        }

        private void InitializePolicies()
        {
            var retryPolicy = Policy.Handle<Exception>()
                .WaitAndRetry(_retryCount, retryAttempt => _retryDelay,
                    (exception, timeSpan, retryCount, context) =>
                    {
                        Log.Logger.Information($"Retry {retryCount} of {_retryCount} after {timeSpan.TotalSeconds}s due to: {exception.Message}");
                    });

            var asyncRetryPolicy = Policy.Handle<Exception>()
                .WaitAndRetryAsync(_retryCount, retryAttempt => _retryDelay,
                    (exception, timeSpan, retryCount, context) =>
                    {
                        Log.Logger.Information($"Retry {retryCount} of {_retryCount} after {timeSpan.TotalSeconds}s due to: {exception.Message}");
                    });

            var timeoutPolicy = Policy.Timeout(_timeoutDuration, TimeoutStrategy.Pessimistic,
                (context, timeSpan, task) =>
                {
                    Log.Logger.Warning($"Execution timed out after {timeSpan.TotalSeconds}s.");
                });

            var asyncTimeoutPolicy = Policy.TimeoutAsync(_timeoutDuration, TimeoutStrategy.Pessimistic,
                (context, timeSpan, task) =>
                {
                    Log.Logger.Warning($"Execution timed out after {timeSpan.TotalSeconds}s.");
                    return Task.CompletedTask;
                });

            var circuitBreakerPolicy = Policy.Handle<Exception>()
                .CircuitBreaker(_circuitBreakerFailures, _circuitBreakerDuration,
                    (exception, duration) =>
                    {
                        Log.Logger.Error($"Circuit broken due to: {exception.Message}. Breaking for {duration.TotalSeconds}s.");
                    },
                    () =>
                    {
                        Log.Logger.Information("Circuit closed.");
                    });

            var asyncCircuitBreakerPolicy = Policy.Handle<Exception>()
                .CircuitBreakerAsync(_circuitBreakerFailures, _circuitBreakerDuration,
                    (exception, duration) =>
                    {
                        Log.Logger.Error($"Circuit broken due to: {exception.Message}. Breaking for {duration.TotalSeconds}s.");
                    },
                    () =>
                    {
                        Log.Logger.Information("Circuit closed.");
                    });

            SyncPolicyWrap = Policy.Wrap(retryPolicy, timeoutPolicy, circuitBreakerPolicy);
            AsyncPolicyWrap = Policy.WrapAsync(asyncRetryPolicy, asyncTimeoutPolicy, asyncCircuitBreakerPolicy);
        }
    }
}