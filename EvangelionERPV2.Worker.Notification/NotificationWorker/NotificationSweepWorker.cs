using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EvangelionERPV2.Worker.NotificationModule.NotificationWorker
{
    /// <summary>
    /// Asks the API to recompute today's alerts and push the tenants whose set changed.
    /// </summary>
    public sealed class NotificationSweepWorker : LeasedScheduleWorker
    {
        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);
        private const int MaxAttempts = 3;

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public NotificationSweepWorker(
            IServiceScopeFactory serviceScopeFactory,
            IConfiguration configuration,
            IDistributedLease lease)
            : base(lease)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        public override string LeaseKey => "eva:lease:notification-sweep";

        public override TimeSpan TimeUntilNextRun(DateTime utcNow) => SweepInterval;

        protected override async Task RunPassAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceScopeFactory.CreateScope();

            for (var attempt = 1; attempt <= MaxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
            {
                try
                {
                    var user = await GetAPIToken(scope);

                    if (user == null || string.IsNullOrWhiteSpace(user.Token))
                    {
                        Log.Logger.Warning("Notification Sweep Worker could not obtain an API token.");
                        return;
                    }

                    var result = await SharedFunctions.PostAsync<NotificationSweepResultDTO>(
                        "Notification/SweepAlerts", new object(), user.Token,
                        cancellationToken: stoppingToken);

                    Log.Logger.Information(
                        "Notification sweep finished. Scanned={Scanned} Notified={Notified} Failed={Failed}",
                        result?.EnterprisesScanned ?? 0,
                        result?.EnterprisesNotified ?? 0,
                        result?.EnterprisesFailed ?? 0);

                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Logger.Error(
                        "Notification Sweep Worker with error. Attempt={Attempt}/{MaxAttempts} ErrorType={ErrorType}",
                        attempt,
                        MaxAttempts,
                        ex.GetType().Name);

                    if (attempt < MaxAttempts)
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        private async Task<UserDTO?> GetAPIToken(IServiceScope scope)
        {
            var kmsProvider = scope.ServiceProvider.GetRequiredService<AWSKMSKeyProvider>();
            var key = await kmsProvider.GetKMSKeyAsync(
                _configuration.GetSection("SelfAPIAuth").Value ?? string.Empty);

            if (!SelfApiCredential.TryParse(key, out var userName, out var password))
            {
                Log.Logger.Warning("Notification Sweep Worker SelfAPIAuth credentials are missing or invalid.");
                return null;
            }

            return await SharedFunctions.PostAsync<UserDTO>(
                "User/LogInto",
                new LoginRequestDTO { UserName = userName, Password = password });
        }
    }
}
