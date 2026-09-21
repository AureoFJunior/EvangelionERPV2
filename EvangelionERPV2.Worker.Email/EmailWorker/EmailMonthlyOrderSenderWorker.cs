using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EvangelionERPV2.Worker.EmailModule.EmailWorker
{
    public sealed class EmailMonthlyOrderSenderWorker : LeasedScheduleWorker
    {
        private const int BroadcastHourUtc = 8;
        private const int MaxAttempts = 3;

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public EmailMonthlyOrderSenderWorker(
            IServiceScopeFactory serviceScopeFactory,
            IConfiguration configuration,
            IDistributedLease lease)
            : base(lease)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        public override string LeaseKey => "eva:lease:email-monthly-order";

        public override TimeSpan TimeUntilNextRun(DateTime utcNow)
            => BroadcastSchedule.TimeUntilNextDaily(utcNow, BroadcastHourUtc);

        protected override async Task RunPassAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceScopeFactory.CreateScope();

            for (var attempt = 1; attempt <= MaxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
            {
                try
                {
                    var user = await GetAPIToken(scope);

                    Log.Logger.Information($"Sending Monthly Order Emails at: {DateTime.UtcNow}");

                    if (user == null || string.IsNullOrWhiteSpace(user.Token))
                    {
                        Log.Logger.Warning("Email Monthly Order Sender Worker could not obtain an API token.");
                    }
                    else
                    {
                        await SharedFunctions.PostAsync<object>("Email/SendMonthlyBillingBroadcast", new object(), user.Token);
                    }

                    Log.Logger.Information($"Email Monthly Order Sender Worker running at: {DateTime.UtcNow}");
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Logger.Error(
                        "Email Monthly Order Sender Worker with error. Attempt={Attempt}/{MaxAttempts} ErrorType={ErrorType}",
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
            var key = kmsProvider.GetKMSKey(_configuration.GetSection("SelfAPIAuth").Value ?? string.Empty);

            if (!SelfApiCredential.TryParse(key, out var userName, out var password))
            {
                Log.Logger.Warning("Email Monthly Order Sender Worker SelfAPIAuth credentials are missing or invalid.");
                return null;
            }

            return await SharedFunctions.PostAsync<UserDTO>(
                "User/LogInto",
                new LoginRequestDTO { UserName = userName, Password = password });
        }
    }
}
