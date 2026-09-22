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
        private readonly IConfiguration _configuration;

        public OrderWorker(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        /// <summary>
        /// One consumer for the worker's lifetime.
        /// <para>
        /// This used to be a loop: take a single message, tear the consumer down, sleep two
        /// seconds, repeat - which capped the queue at roughly 0.4 messages a second no matter how
        /// deep it was. The remaining delay below is a RECONNECT BACKOFF, not a poll interval; it
        /// runs only when the consumer has fallen over.
        /// </para>
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var rabbitMQManager = scope.ServiceProvider.GetRequiredService<IRabbitMQManager>();
                    var kmsProvider = scope.ServiceProvider.GetRequiredService<AWSKMSKeyProvider>();
                    var key = kmsProvider.GetKMSKey(_configuration.GetSection("SelfAPIAuth").Value ?? string.Empty);

                    Log.Logger.Information("Order Worker consuming from {Time}", DateTime.UtcNow);

                    await rabbitMQManager.ConsumeAsync<CreateQueuedOrderRequestDTO>(async (order, messageId) =>
                    {
                        var token = await GetCachedApiTokenAsync(key, stoppingToken);
                        if (string.IsNullOrWhiteSpace(token))
                            throw new InvalidOperationException("Order Worker could not obtain an API token.");

                        var request = ValidateQueuedOrder(order);
                        request.MessageId = messageId;
                        var createdOrder = await SharedFunctions.PostAsync<object>("Order/InsertQueuedOrder", request, token, cancellationToken: stoppingToken);
                        if (createdOrder == null)
                            throw new InvalidOperationException("Order Worker failed to persist order through API.");
                    }, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Logger.Error("Order Worker with error. ErrorType={ErrorType}", ex.GetType().Name);

                    try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        /// <summary>
        /// Caches the self-API token instead of logging in for every message.
        /// <para>
        /// Once the consumer stopped serving one message at a time, the per-message
        /// <c>User/LogInto</c> became the next ceiling rather than the queue: that endpoint is rate
        /// limited to 100 a minute, so a queue draining faster than that would have answered 429 and
        /// failed every order. The lock keeps a burst of concurrent handlers from all logging in at
        /// once on a cold cache.
        /// </para>
        /// <para>
        /// The lifetime is deliberately shorter than the token's own: an expired token surfaces as a
        /// failed message that the retry ladder replays, which is survivable, whereas holding one
        /// too long would fail every message in flight at the same moment.
        /// </para>
        /// </summary>
        private static readonly SemaphoreSlim TokenLock = new(1, 1);
        private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
        private string? _cachedToken;

        private long _cachedTokenExpiresAtTicks;

        private async Task<string?> GetCachedApiTokenAsync(string key, CancellationToken cancellationToken)
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

                var user = await GetAPIToken(key, cancellationToken);
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

        private async Task<UserDTO?> GetAPIToken(string key, CancellationToken cancellationToken)
        {
            var loginRequest = BuildLoginRequest(key);
            if (loginRequest == null)
            {
                Log.Logger.Warning("Order Worker SelfAPIAuth credentials are missing or invalid.");
                return null;
            }

            return await SharedFunctions.PostAsync<UserDTO>("User/LogInto", loginRequest, cancellationToken: cancellationToken);
        }

        private static CreateQueuedOrderRequestDTO ValidateQueuedOrder(CreateQueuedOrderRequestDTO order)
        {
            if (order == null)
                throw new InvalidOperationException("Queued order payload is required.");

            if (order.EnterpriseId == Guid.Empty)
                throw new InvalidOperationException("Queued order enterprise is required.");

            if (order.UserId == Guid.Empty)
                throw new InvalidOperationException("Queued order user is required.");

            if (order.CustomerId == Guid.Empty)
                throw new InvalidOperationException("Queued order customer is required.");

            if (!order.Items.Any())
                throw new InvalidOperationException("Queued order must include at least one item.");

            return order;
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
