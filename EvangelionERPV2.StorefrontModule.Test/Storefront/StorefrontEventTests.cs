using System.Net;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.StorefrontModule.Application.Workers;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.StorefrontModule.Test
{
    /// <summary>Review spec L4: the ERP tells Limen when a store ships or refunds, signed, retried, once.</summary>
    public class StorefrontEventTests : IDisposable
    {
        private readonly StorefrontTestContext _database = new();
        private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        private static readonly StorefrontEventSettings Settings = new(new Uri("http://limen.test/erp-events"), new string('s', 32));

        public void Dispose() => _database.Dispose();

        private static Order LimenOrder(EnumOrderStatus status, string? tracking = null) => new()
        {
            Id = Guid.NewGuid(), Channel = (int)EnumOrderChannel.Storefront, Status = (int)status, TrackingCode = tracking, ExternalReference = Guid.NewGuid().ToString()
        };

        [Fact]
        public void ShippingALimenOrder_OrCorrectingItsTracking_RaisesOneEvent_AndNothingElseDoes()
        {
            Assert.Equal(StorefrontEvents.Shipped, StorefrontEvents.ForUpdate(LimenOrder(EnumOrderStatus.Paid), (int)EnumOrderStatus.Shipped, "BR1"));
            Assert.Equal(StorefrontEvents.Shipped, StorefrontEvents.ForUpdate(LimenOrder(EnumOrderStatus.Shipped, "BR1"), (int)EnumOrderStatus.Shipped, "BR2"));
            Assert.Null(StorefrontEvents.ForUpdate(LimenOrder(EnumOrderStatus.Shipped, "BR1"), (int)EnumOrderStatus.Shipped, "BR1"));
            Assert.Null(StorefrontEvents.ForUpdate(LimenOrder(EnumOrderStatus.Paid), (int)EnumOrderStatus.Processing, null));

            var manual = LimenOrder(EnumOrderStatus.Paid);
            manual.Channel = (int)EnumOrderChannel.Erp;
            Assert.Null(StorefrontEvents.ForUpdate(manual, (int)EnumOrderStatus.Shipped, "BR1"));
        }

        [Fact]
        public void TheSignature_IsTheHexHmacSha256OfTheBody()
        {
            // echo -n '{"a":1}' | openssl dgst -sha256 -hmac key
            Assert.Equal("sha256=88a67f24bbcdaed0e6c997404bb79a743baf44c6bab2f4c27328e3009d22e342", StorefrontEvents.Sign("key", "{\"a\":1}"));
            Assert.Equal(StorefrontEvents.Sign("key", "{\"a\":1}"), StorefrontEvents.Sign("key", "{\"a\":1}"));
            Assert.NotEqual(StorefrontEvents.Sign("key", "{\"a\":1}"), StorefrontEvents.Sign("key", "{\"a\":2}"));
            Assert.NotEqual(StorefrontEvents.Sign("key", "{\"a\":1}"), StorefrontEvents.Sign("other", "{\"a\":1}"));
        }

        [Theory]
        [InlineData(1, 30)]
        [InlineData(2, 60)]
        [InlineData(5, 480)]
        [InlineData(20, 3600)]
        public void Retries_BackOffUpToAnHour(int attempts, int seconds) =>
            Assert.Equal(Now.AddSeconds(seconds), StorefrontEvents.NextAttempt(attempts, Now));

        private sealed class Limen(Func<HttpRequestMessage, string, HttpStatusCode> answer) : HttpMessageHandler
        {
            public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Calls.Add((request, body));
                return new HttpResponseMessage(answer(request, body));
            }
        }

        private async Task<StorefrontEvent> SeedAsync(DateTime createdAt)
        {
            var order = LimenOrder(EnumOrderStatus.Paid);
            var pending = StorefrontEvents.Create(StorefrontEvents.Shipped, order, createdAt);
            await _database.SeedAsync(pending);
            return pending;
        }

        [Fact]
        public async Task ADueEvent_IsPostedSigned_AndMarkedDelivered()
        {
            var pending = await SeedAsync(Now);
            var limen = new Limen((_, _) => HttpStatusCode.Accepted);

            var delivered = await StorefrontEventDispatcher.DispatchPendingAsync(_database.CreateContext(), new HttpClient(limen), Settings, Now, default);

            Assert.Equal(1, delivered);
            var (request, body) = limen.Calls.Single();
            Assert.Equal(Settings.EventsUrl, request.RequestUri);
            Assert.Equal(StorefrontEvents.Sign(Settings.Secret, body), request.Headers.GetValues("X-Limen-Signature").Single());
            Assert.Contains($"\"eventId\":\"{pending.Id}\"", body);
            Assert.Contains("\"type\":\"order.shipped\"", body);
            using var context = _database.CreateContext();
            Assert.Equal(Now, (await context.StorefrontEvent.SingleAsync()).DeliveredAt);

            await StorefrontEventDispatcher.DispatchPendingAsync(_database.CreateContext(), new HttpClient(limen), Settings, Now.AddMinutes(1), default);
            Assert.Single(limen.Calls);
        }

        [Fact]
        public async Task ARefusedEvent_IsRetriedLater_AndGivenUpOnAfterADay()
        {
            await SeedAsync(Now);
            var limen = new Limen((_, _) => HttpStatusCode.InternalServerError);

            await StorefrontEventDispatcher.DispatchPendingAsync(_database.CreateContext(), new HttpClient(limen), Settings, Now, default);
            using (var context = _database.CreateContext())
            {
                var row = await context.StorefrontEvent.SingleAsync();
                Assert.Equal((1, Now.AddSeconds(30), (DateTime?)null, (DateTime?)null), (row.Attempts, row.NextAttemptAt, row.DeliveredAt, row.FailedAt));
            }

            await StorefrontEventDispatcher.DispatchPendingAsync(_database.CreateContext(), new HttpClient(limen), Settings, Now.AddSeconds(10), default);
            Assert.Single(limen.Calls);

            await StorefrontEventDispatcher.DispatchPendingAsync(_database.CreateContext(), new HttpClient(limen), Settings, Now.AddHours(25), default);
            using var check = _database.CreateContext();
            Assert.Equal(Now.AddHours(25), (await check.StorefrontEvent.SingleAsync()).FailedAt);
        }
    }
}
