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

        public PolicyWrap SyncPolicyWrap { get; private set; } = null!;
        public AsyncPolicyWrap AsyncPolicyWrap { get; private set; } = null!;

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
                        Log.Logger.Information(
                            "Retry {RetryCount} of {MaxRetries} after {DelaySeconds}s. ErrorType={ErrorType}",
                            retryCount,
                            _retryCount,
                            timeSpan.TotalSeconds,
                            GetSafeExceptionType(exception));
                    });

            var asyncRetryPolicy = Policy.Handle<Exception>()
                .WaitAndRetryAsync(_retryCount, retryAttempt => _retryDelay,
                    (exception, timeSpan, retryCount, context) =>
                    {
                        Log.Logger.Information(
                            "Retry {RetryCount} of {MaxRetries} after {DelaySeconds}s. ErrorType={ErrorType}",
                            retryCount,
                            _retryCount,
                            timeSpan.TotalSeconds,
                            GetSafeExceptionType(exception));
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
                        Log.Logger.Error(
                            "Circuit broken. ErrorType={ErrorType}. Breaking for {DurationSeconds}s.",
                            GetSafeExceptionType(exception),
                            duration.TotalSeconds);
                    },
                    () =>
                    {
                        Log.Logger.Information("Circuit closed.");
                    });

            var asyncCircuitBreakerPolicy = Policy.Handle<Exception>()
                .CircuitBreakerAsync(_circuitBreakerFailures, _circuitBreakerDuration,
                    (exception, duration) =>
                    {
                        Log.Logger.Error(
                            "Circuit broken. ErrorType={ErrorType}. Breaking for {DurationSeconds}s.",
                            GetSafeExceptionType(exception),
                            duration.TotalSeconds);
                    },
                    () =>
                    {
                        Log.Logger.Information("Circuit closed.");
                    });

            SyncPolicyWrap = Policy.Wrap(retryPolicy, timeoutPolicy, circuitBreakerPolicy);
            AsyncPolicyWrap = Policy.WrapAsync(asyncRetryPolicy, asyncTimeoutPolicy, asyncCircuitBreakerPolicy);
        }

        private static string GetSafeExceptionType(Exception? exception)
        {
            return exception?.GetType().Name ?? "UnknownError";
        }
    }
}
