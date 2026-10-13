using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EvangelionERPV2.Worker.EmailModule.EmailWorker
{
    public sealed class EmailStockSenderWorker : LeasedScheduleWorker
    {
        private const DayOfWeek BroadcastDay = DayOfWeek.Monday;
        private const int BroadcastHourUtc = 8;
        private const int MaxAttempts = 3;

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public EmailStockSenderWorker(
            IServiceScopeFactory serviceScopeFactory,
            IConfiguration configuration,
            IDistributedLease lease)
            : base(lease)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        public override string LeaseKey => "eva:lease:email-weekly-stock";

        public override TimeSpan TimeUntilNextRun(DateTime utcNow)
            => BroadcastSchedule.TimeUntilNextWeekly(utcNow, BroadcastDay, BroadcastHourUtc);

        protected override async Task RunPassAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceScopeFactory.CreateScope();

            for (var attempt = 1; attempt <= MaxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
            {
                try
                {
                    var user = await GetAPIToken(scope);

                    Log.Logger.Information($"Sending Stock Emails at: {DateTime.UtcNow}");

                    if (user == null || string.IsNullOrWhiteSpace(user.Token))
                    {
                        Log.Logger.Warning("Email Stock Sender Worker could not obtain an API token.");
                    }
                    else
                    {
                        await SharedFunctions.PostAsync<object>(
                            "Email/SendWeeklyStockBroadcast", new object(), user.Token,
                            cancellationToken: stoppingToken);
                    }

                    Log.Logger.Information($"Email Stock Sender Worker running at: {DateTime.UtcNow}");
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Logger.Error(
                        "Email Stock Sender Worker with error. Attempt={Attempt}/{MaxAttempts} ErrorType={ErrorType}",
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
                Log.Logger.Warning("Email Stock Sender Worker SelfAPIAuth credentials are missing or invalid.");
                return null;
            }

            return await SharedFunctions.PostAsync<UserDTO>(
                "User/LogInto",
                new LoginRequestDTO { UserName = userName, Password = password });
        }
    }
}
