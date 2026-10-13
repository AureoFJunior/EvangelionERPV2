using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EvangelionERPV2.StorefrontModule.Application.Workers
{
    /// <summary>Where Storefront.Api takes ERP events and the secret both sides sign them with.</summary>
    public sealed record StorefrontEventSettings(Uri EventsUrl, string Secret);

    /// <summary>
    /// Posts pending <see cref="Shared.Entities.StorefrontEvent"/> rows to Storefront.Api
    /// (<c>POST /erp-events</c>), signed <c>X-Limen-Signature</c>. A delivery that fails is retried with
    /// backoff; after a day it is given up on and logged. Once per slot across replicas (the lease).
    /// </summary>
    public sealed class StorefrontEventDispatcher : LeasedScheduleWorker
    {
        private const int BatchSize = 50;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly StorefrontEventSettings _settings;

        public StorefrontEventDispatcher(IServiceScopeFactory scopeFactory, IDistributedLease lease, IHttpClientFactory httpClientFactory, StorefrontEventSettings settings)
            : base(lease)
        {
            _scopeFactory = scopeFactory;
            _httpClientFactory = httpClientFactory;
            _settings = settings;
        }

        public override string LeaseKey => "eva:lease:storefront-events";

        public override TimeSpan TimeUntilNextRun(DateTime utcNow) => TimeSpan.FromSeconds(15);

        protected override async Task RunPassAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await DispatchPendingAsync(context, _httpClientFactory.CreateClient(nameof(StorefrontEventDispatcher)), _settings, DateTime.UtcNow, stoppingToken);
        }

        /// <summary>One pass: every due event, oldest first, at most <see cref="BatchSize"/>. Answers how many were delivered.</summary>
        public static async Task<int> DispatchPendingAsync(AppDbContext context, HttpClient http, StorefrontEventSettings settings, DateTime now, CancellationToken cancellationToken)
        {
            var due = await context.StorefrontEvent
                .Where(e => e.DeliveredAt == null && e.FailedAt == null && e.NextAttemptAt <= now)
                .OrderBy(e => e.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            var delivered = 0;
            foreach (var pending in due)
            {
                var body = JsonSerializer.Serialize(new
                {
                    eventId = pending.Id,
                    type = pending.Type,
                    checkoutId = pending.CheckoutId,
                    orderNumber = pending.OrderNumber,
                    occurredAt = pending.CreatedAt
                });

                bool ok;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, settings.EventsUrl)
                    {
                        Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json"))
                    };
                    request.Headers.Add("X-Limen-Signature", StorefrontEvents.Sign(settings.Secret, body));
                    using var response = await http.SendAsync(request, cancellationToken);
                    ok = response.IsSuccessStatusCode;
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    ok = false;
                }

                pending.UpdatedAt = now;
                if (ok)
                {
                    pending.DeliveredAt = now;
                    delivered++;
                }
                else
                {
                    pending.Attempts++;
                    pending.NextAttemptAt = StorefrontEvents.NextAttempt(pending.Attempts, now);
                    if (now - pending.CreatedAt >= StorefrontEvents.GiveUpAfter)
                    {
                        pending.FailedAt = now;
                        Log.Logger.Error("Limen event [{EventId}] {Type} for {OrderNumber} was not accepted in {Attempts} attempts; giving up",
                            pending.Id, pending.Type, pending.OrderNumber, pending.Attempts);
                    }
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            return delivered;
        }
    }
}
