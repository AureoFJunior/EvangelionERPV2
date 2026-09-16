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

        public EmailConsumerWorker(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        /// <summary>
        /// One consumer for the worker's lifetime. This was a loop that served a single message and
        /// then slept five seconds, capping the email queue near 0.2 messages a second regardless of
        /// depth. The delay that remains is a reconnect backoff, not a poll interval.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var rabbitMQManager = scope.ServiceProvider.GetRequiredService<IRabbitMQManager>();

                    Log.Logger.Information("Email Consumer Worker consuming from {Time}", DateTime.UtcNow);

                    await rabbitMQManager.ConsumeAsync<string>(async rawMessage =>
                    {
                        if (string.IsNullOrWhiteSpace(rawMessage))
                            throw new InvalidOperationException("Email Consumer Worker received an empty message payload.");

                        var token = await GetCachedApiTokenAsync(scope, stoppingToken);
                        if (string.IsNullOrWhiteSpace(token))
                            throw new InvalidOperationException("Email Consumer Worker could not obtain an API token.");

                        var sentEmail = await SharedFunctions.PostAsync<object>("Email/SendQueuedEmail", rawMessage, token, cancellationToken: stoppingToken);
                        if (sentEmail == null)
                            throw new InvalidOperationException("Email Consumer Worker failed to send email through API.");
                    }, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Logger.Error("Email Consumer Worker error. ErrorType={ErrorType}", ex.GetType().Name);

                    try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        /// <summary>
        /// Caches the self-API token rather than logging in per message. User/LogInto is rate
        /// limited to 100 a minute, which becomes the ceiling the moment the consumer is able to
        /// drain faster than that.
        /// </summary>
        private static readonly SemaphoreSlim TokenLock = new(1, 1);
        private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
        private string? _cachedToken;

        // Ticks in a long, read with Interlocked: handlers now run concurrently, and the fast path
        // below reads both fields outside the lock. A DateTime field is not guaranteed to be read
        // atomically, so a torn read could accept an expired token or reject a live one.
        private long _cachedTokenExpiresAtTicks;

        private async Task<string?> GetCachedApiTokenAsync(IServiceScope scope, CancellationToken cancellationToken)
        {
            var cached = Volatile.Read(ref _cachedToken);
            if (cached is not null && DateTime.UtcNow.Ticks < Interlocked.Read(ref _cachedTokenExpiresAtTicks))
                return cached;

            await TokenLock.WaitAsync(cancellationToken);
            try
            {
                cached = Volatile.Read(ref _cachedToken);
                if (cached is not null && DateTime.UtcNow.Ticks < Interlocked.Read(ref _cachedTokenExpiresAtTicks))
                    return cached;

                var user = await GetAPIToken(scope, cancellationToken);
                if (user is null || string.IsNullOrWhiteSpace(user.Token))
                    return null;

                Volatile.Write(ref _cachedToken, user.Token);
                Interlocked.Exchange(ref _cachedTokenExpiresAtTicks, DateTime.UtcNow.Add(TokenLifetime).Ticks);
                return user.Token;
            }
            finally
            {
                TokenLock.Release();
            }
        }

        private async Task<UserDTO?> GetAPIToken(IServiceScope scope, CancellationToken cancellationToken)
        {
            string key = string.Empty;

            var kmsProvider = scope.ServiceProvider.GetRequiredService<AWSKMSKeyProvider>();
            key = kmsProvider.GetKMSKey(_configuration.GetSection("SelfAPIAuth").Value ?? string.Empty);

            var loginRequest = BuildLoginRequest(key);
            if (loginRequest == null)
            {
                Log.Logger.Warning("Email Consumer Worker SelfAPIAuth credentials are missing or invalid.");
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
