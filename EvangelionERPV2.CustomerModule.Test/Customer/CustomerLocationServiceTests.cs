using System.Linq.Expressions;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.CustomerModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Moq;

namespace EvangelionERPV2.CustomerModule.Test
{
    public class CustomerLocationServiceTests
    {
        private static readonly Guid EnterpriseId = Guid.NewGuid();

        // Praça da Sé, São Paulo.
        private const decimal OriginLatitude = -23.550520m;
        private const decimal OriginLongitude = -46.633308m;

        private readonly Mock<IRepository<Customer>> _customers = new();
        private readonly Mock<ICepGeocoder> _geocoder = new();

        private CustomerLocationService BuildService() => new(_customers.Object, _geocoder.Object);

        private void GivenCustomers(params Customer[] customers)
        {
            _customers
                .Setup(r => r.GetPagedAsync(
                    It.IsAny<Expression<Func<Customer, bool>>?>(),
                    It.IsAny<Func<IQueryable<Customer>, IOrderedQueryable<Customer>>?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync(((IReadOnlyList<Customer>)customers.ToList(), customers.Length));
        }

        private static Customer At(string name, decimal latitude, decimal longitude, string? cep = null) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            IsActive = true,
            Name = name,
            PhoneNumber = "11999998888",
            Latitude = latitude,
            Longitude = longitude,
            Cep = cep
        };

        [Fact]
        public void Haversine_MatchesAKnownDistance()
        {
            // Praça da Sé to Cristo Redentor is about 360 km in a straight line.
            var km = GeoDistance.KilometresBetween(-23.550520, -46.633308, -22.951916, -43.210487);

            Assert.InRange(km, 355, 365);
        }

        [Fact]
        public void BoundingBox_WidensWithLatitude_AndDoesNotBlowUpAtThePole()
        {
            var equator = GeoDistance.BoundingBox(0, 0, 100);
            var high = GeoDistance.BoundingBox(60, 0, 100);
            var pole = GeoDistance.BoundingBox(89.999, 0, 100);

            // A degree of longitude is shorter further from the equator, so the same radius needs a
            // wider span of degrees to cover it.
            Assert.True(high.MaxLongitude - high.MinLongitude > equator.MaxLongitude - equator.MinLongitude);

            // At the pole cos(latitude) approaches zero; the span is clamped instead of dividing by it.
            Assert.Equal(-180, pole.MinLongitude);
            Assert.Equal(180, pole.MaxLongitude);
        }

        [Fact]
        public async Task Nearby_CutsTheCornersTheBoundingBoxLetThrough()
        {
            // ~1 km away, and ~40 km away. With a 5 km radius the far one is inside the box the
            // database filtered but outside the circle.
            var near = At("Perto", -23.5595m, -46.6335m);
            var far = At("Longe", -23.9000m, -46.6335m);

            GivenCustomers(near, far);

            var result = await BuildService().GetNearbyAsync(EnterpriseId, OriginLatitude, OriginLongitude, radiusKm: 5, take: 50);

            Assert.Single(result);
            Assert.Equal("Perto", result[0].CustomerName);
            Assert.True(result[0].DistanceKm > 0);
        }

        [Fact]
        public async Task Nearby_OrdersByDistanceAndHonoursTheCount()
        {
            GivenCustomers(
                At("Terceiro", -23.5900m, -46.6335m),
                At("Primeiro", -23.5510m, -46.6333m),
                At("Segundo", -23.5600m, -46.6335m));

            var result = await BuildService().GetNearbyAsync(EnterpriseId, OriginLatitude, OriginLongitude, radiusKm: 50, take: 2);

            Assert.Equal(2, result.Count);
            Assert.Equal("Primeiro", result[0].CustomerName);
            Assert.Equal("Segundo", result[1].CustomerName);
        }

        [Fact]
        public async Task Nearby_RefusesAnImpossiblePointOrRadius()
        {
            GivenCustomers();

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => BuildService().GetNearbyAsync(EnterpriseId, 200m, 0m, 10, 10));
            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => BuildService().GetNearbyAsync(EnterpriseId, OriginLatitude, OriginLongitude, 0, 10));
        }

        [Fact]
        public async Task Backfill_LocatesWhatItCanAndLeavesTheRestAlone()
        {
            var locatable = At("Com CEP", 0m, 0m, "01001-000");
            locatable.Latitude = null;
            locatable.Longitude = null;

            var unknown = At("CEP desconhecido", 0m, 0m, "99999-999");
            unknown.Latitude = null;
            unknown.Longitude = null;

            GivenCustomers(locatable, unknown);
            _customers.Setup(r => r.Update(It.IsAny<Customer>())).Returns((Customer customer) => customer);

            _geocoder.Setup(g => g.ResolveAsync("01001-000", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GeocodedPoint(-23.550520m, -46.633308m));
            _geocoder.Setup(g => g.ResolveAsync("99999-999", It.IsAny<CancellationToken>()))
                .ReturnsAsync((GeocodedPoint?)null);

            var result = await BuildService().GeocodePendingAsync(EnterpriseId, take: 25);

            Assert.Equal(2, result.Examined);
            Assert.Equal(1, result.Located);
            Assert.Equal(-23.550520m, locatable.Latitude);
            // A CEP the service cannot place stays null rather than getting a made-up point.
            Assert.Null(unknown.Latitude);
        }

        [Fact]
        public async Task Backfill_CommitsNothingWhenItLocatedNothing()
        {
            var pending = At("Sem sorte", 0m, 0m, "99999-999");
            pending.Latitude = null;
            pending.Longitude = null;

            GivenCustomers(pending);
            _geocoder.Setup(g => g.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((GeocodedPoint?)null);

            await BuildService().GeocodePendingAsync(EnterpriseId, take: 25);

            _customers.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
