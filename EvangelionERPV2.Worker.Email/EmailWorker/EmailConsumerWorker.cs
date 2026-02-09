using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Worker.EmailModule.EmailWorker.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MimeKit;
using Serilog;
using System.Text.Json;

namespace EvangelionERPV2.Worker.EmailModule.EmailWorker
{
    public sealed class EmailConsumerWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        private AWSKMSKeyProvider _kmsProvider;

        public EmailConsumerWorker(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
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
                    using (var scope = _serviceScopeFactory.CreateScope())
                    {
                        try
                        {
                            var rabbitMQManager = scope.ServiceProvider.GetRequiredService<IRabbitMQManager>();

                            // Get from Email Queue and send
                            var rawMessage = await rabbitMQManager.DequeueAndProcessAsync<string>();

                            if (!string.IsNullOrEmpty(rawMessage))
                            {
                                UserDTO? user;
                                user = await GetAPIToken(scope);

                                Log.Logger.Information($"Sending Email at: {DateTime.UtcNow}");

                                if (user == null || string.IsNullOrWhiteSpace(user.Token))
                                {
                                    Log.Logger.Warning("Email Consumer Worker could not obtain an API token.");
                                }
                                else
                                {
                                    await SharedFunctions.PostAsync<object>("Email/SendEmail", rawMessage, user.Token);
                                }
                            }

                            Log.Logger.Information($"Email Consumer Worker running at: {DateTime.UtcNow}");
                        }
                        catch (Exception ex)
                        {
                            Log.Logger.Error($"Email Consumer Worker error: {ex.Message}", ex.Message, ex.InnerException);
                        }
                        finally
                        {
                            // Delay before the next loop iteration
                            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Email Consumer With Scope error: {ex.Message}", ex.Message, ex.InnerException);
            }
        }

        private async Task<UserDTO?> GetAPIToken(IServiceScope scope)
        {
            string key = string.Empty;

            _kmsProvider = scope.ServiceProvider.GetRequiredService<AWSKMSKeyProvider>();
            key = _kmsProvider.GetKMSKey(_configuration.GetSection("SelfAPIAuth").Value ?? string.Empty);

            var loginRequest = BuildLoginRequest(key);
            if (loginRequest == null)
            {
                Log.Logger.Warning("Email Consumer Worker SelfAPIAuth credentials are missing or invalid.");
                return null;
            }

            return await SharedFunctions.PostAsync<UserDTO>("User/LogInto", loginRequest);
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
