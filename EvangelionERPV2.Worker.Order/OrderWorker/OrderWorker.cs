using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Worker.OrderModule.OrderWorker.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Text.Json;

namespace EvangelionERPV2.Worker.OrderModule.OrderWorker
{
    public sealed class OrderWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private AWSKMSKeyProvider _kmsProvider;
        private readonly IConfiguration _configuration;

        public OrderWorker(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                string key = string.Empty;
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using (var messageScope = _serviceScopeFactory.CreateScope())
                        {
                            var scopedRabbitMQ = messageScope.ServiceProvider.GetRequiredService<IRabbitMQManager>();
                            _kmsProvider = messageScope.ServiceProvider.GetRequiredService<AWSKMSKeyProvider>();
                            key = _kmsProvider.GetKMSKey(_configuration.GetSection("SelfAPIAuth").Value ?? string.Empty);

                            await scopedRabbitMQ.DequeueAndProcessAsync<Order>(async order =>
                            {
                                Log.Logger.Information($"Creating Order at: {DateTime.UtcNow}");

                                var user = await GetAPIToken(messageScope, key, stoppingToken);
                                if (user == null || string.IsNullOrWhiteSpace(user.Token))
                                    throw new InvalidOperationException("Order Worker could not obtain an API token.");

                                var createdOrder = await SharedFunctions.PostAsync<object>("Order/InsertOrder", order, user.Token, cancellationToken: stoppingToken);
                                if (createdOrder == null)
                                    throw new InvalidOperationException("Order Worker failed to persist order through API.");
                            }, stoppingToken);

                            Log.Logger.Information($"Order Worker running at: {DateTime.UtcNow}");
                        }

                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error("Order Worker with error. ErrorType={ErrorType}", ex.GetType().Name);
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

                    }
                }   
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Order Worker scope with error. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }

        private async Task<UserDTO?> GetAPIToken(IServiceScope scope, string key, CancellationToken cancellationToken)
        {
            var loginRequest = BuildLoginRequest(key);
            if (loginRequest == null)
            {
                Log.Logger.Warning("Order Worker SelfAPIAuth credentials are missing or invalid.");
                return null;
            }

            return await SharedFunctions.PostAsync<UserDTO>("User/LogInto", loginRequest, cancellationToken: cancellationToken);
        }

        private static LoginRequestDTO? BuildLoginRequest(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            var trimmed = key.Trim();
            if (TryParseJsonCredentials(trimmed, out var userName, out var password))
            {
                return new LoginRequestDTO
                {
                    UserName = userName,
                    Password = password
                };
            }

            if (TryParseKeyValueCredentials(trimmed, out userName, out password))
            {
                return new LoginRequestDTO
                {
                    UserName = userName,
                    Password = password
                };
            }

            var separators = new[] { "|", ";", ":", "/" };
            foreach (var separator in separators)
            {
                var index = trimmed.IndexOf(separator, StringComparison.Ordinal);
                if (index > 0 && index < trimmed.Length - 1)
                {
                    var parsedUserName = trimmed.Substring(0, index).Trim();
                    var parsedPassword = trimmed.Substring(index + 1).Trim();
                    if (!string.IsNullOrWhiteSpace(parsedUserName) && !string.IsNullOrWhiteSpace(parsedPassword))
                    {
                        return new LoginRequestDTO
                        {
                            UserName = parsedUserName,
                            Password = parsedPassword
                        };
                    }
                }
            }

            return null;
        }

        private static bool TryParseJsonCredentials(string input, out string userName, out string password)
        {
            userName = string.Empty;
            password = string.Empty;

            if (!input.StartsWith("{", StringComparison.Ordinal))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(input);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return false;

                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String)
                        continue;

                    var key = property.Name;
                    var value = property.Value.GetString();
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    if (IsUserNameKey(key))
                        userName = value;
                    else if (IsPasswordKey(key))
                        password = value;
                }
            }
            catch (JsonException)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(password);
        }

        private static bool TryParseKeyValueCredentials(string input, out string userName, out string password)
        {
            userName = string.Empty;
            password = string.Empty;

            var pairs = input.Split(new[] { ';', '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                    continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (IsUserNameKey(key))
                    userName = value;
                else if (IsPasswordKey(key))
                    password = value;
            }

            return !string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(password);
        }

        private static bool IsUserNameKey(string key)
        {
            return key.Equals("username", StringComparison.OrdinalIgnoreCase)
                || key.Equals("user", StringComparison.OrdinalIgnoreCase)
                || key.Equals("login", StringComparison.OrdinalIgnoreCase)
                || key.Equals("email", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPasswordKey(string key)
        {
            return key.Equals("password", StringComparison.OrdinalIgnoreCase)
                || key.Equals("pass", StringComparison.OrdinalIgnoreCase)
                || key.Equals("pwd", StringComparison.OrdinalIgnoreCase);
        }
    }
}
