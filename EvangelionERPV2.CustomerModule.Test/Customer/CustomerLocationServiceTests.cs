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

        private const decimal OriginLatitude = -23.550520m;
        private const decimal OriginLongitude = -46.633308m;

        private readonly Mock<IRepository<Customer>> _customers = new();
        private readonly Mock<IAddressGeocoder> _geocoder = new();

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

        /// <summary>
        /// Like <see cref="GivenCustomers"/>, but runs the predicate the service built instead of
        /// ignoring it. The place filter lives in that expression and nowhere else, so a mock that
        /// answers every query with the same list cannot tell whether it works.
        /// </summary>
        private void GivenFilteredCustomers(params Customer[] customers)
        {
            _customers
                .Setup(r => r.GetPagedAsync(
                    It.IsAny<Expression<Func<Customer, bool>>?>(),
                    It.IsAny<Func<IQueryable<Customer>, IOrderedQueryable<Customer>>?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync((
                    Expression<Func<Customer, bool>>? predicate,
                    Func<IQueryable<Customer>, IOrderedQueryable<Customer>>? _,
                    int? __,
                    int? ___) =>
                {
                    var match = predicate?.Compile() ?? (customer => true);
                    var filtered = customers.Where(match).ToList();
                    return ((IReadOnlyList<Customer>)filtered, filtered.Count);
                });
        }

        private static Customer In(string name, string? bairro, string? municipio, string? uf,
            decimal? latitude = null, decimal? longitude = null) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            IsActive = true,
            Name = name,
            PhoneNumber = "11999998888",
            Bairro = bairro,
            Municipio = municipio,
            Uf = uf,
            Latitude = latitude,
            Longitude = longitude
        };

        private static Customer At(string name, decimal latitude, decimal longitude, string? municipio = null) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            IsActive = true,
            Name = name,
            PhoneNumber = "11999998888",
            Latitude = latitude,
            Longitude = longitude,
            Municipio = municipio
        };

        [Fact]
        public void Haversine_MatchesAKnownDistance()
        {
            var km = GeoDistance.KilometresBetween(-23.550520, -46.633308, -22.951916, -43.210487);

            Assert.InRange(km, 355, 365);
        }

        [Fact]
        public void BoundingBox_WidensWithLatitude_AndDoesNotBlowUpAtThePole()
        {
            var equator = GeoDistance.BoundingBox(0, 0, 100);
            var high = GeoDistance.BoundingBox(60, 0, 100);
            var pole = GeoDistance.BoundingBox(89.999, 0, 100);

            Assert.True(high.MaxLongitude - high.MinLongitude > equator.MaxLongitude - equator.MinLongitude);

            Assert.Equal(-180, pole.MinLongitude);
            Assert.Equal(180, pole.MaxLongitude);
        }

        [Fact]
        public async Task Nearby_CutsTheCornersTheBoundingBoxLetThrough()
        {
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
        public async Task NearbyByPlace_MatchesCityStateOrNeighbourhood_CaseInsensitively()
        {
            var campinas = In("Padaria de Campinas", "Cambuí", "Campinas", "SP", -22.9050m, -47.0600m);
            var santos = In("Mercado de Santos", "Gonzaga", "Santos", "SP", -23.9670m, -46.3330m);
            var curitiba = In("Loja de Curitiba", "Batel", "Curitiba", "PR", -25.4290m, -49.2710m);

            GivenFilteredCustomers(campinas, santos, curitiba);
            var service = BuildService();

            var byCity = await service.GetNearbyAsync(
                EnterpriseId, OriginLatitude, OriginLongitude, radiusKm: 5, take: 50, place: "CAMPINAS");
            var byNeighbourhood = await service.GetNearbyAsync(
                EnterpriseId, OriginLatitude, OriginLongitude, radiusKm: 5, take: 50, place: "gonzaga");
            var byState = await service.GetNearbyAsync(
                EnterpriseId, OriginLatitude, OriginLongitude, radiusKm: 5, take: 50, place: "pr");

            Assert.Equal("Padaria de Campinas", Assert.Single(byCity).CustomerName);
            Assert.Equal("Mercado de Santos", Assert.Single(byNeighbourhood).CustomerName);
            Assert.Equal("Loja de Curitiba", Assert.Single(byState).CustomerName);
        }

        [Fact]
        public async Task NearbyByPlace_ReturnsTheCustomersNobodyCouldPin_Last()
        {
            var located = In("Com coordenada", "Centro", "Campinas", "SP", -22.9050m, -47.0600m);
            var unlocated = In("Sem coordenada", "Centro", "Campinas", "SP");

            GivenFilteredCustomers(located, unlocated);

            var result = await BuildService().GetNearbyAsync(
                EnterpriseId, OriginLatitude, OriginLongitude, radiusKm: 50, take: 50, place: "campinas");

            Assert.Equal(2, result.Count);
            Assert.Equal("Com coordenada", result[0].CustomerName);
            Assert.Equal("Sem coordenada", result[1].CustomerName);
            Assert.NotNull(result[0].DistanceKm);
            Assert.Null(result[1].DistanceKm);
            Assert.Null(result[1].Latitude);
        }

        [Fact]
        public async Task NearbyByRadius_StillRefusesACustomerWithNoCoordinates()
        {
            var unlocated = In("Sem coordenada", "Sé", "São Paulo", "SP");

            GivenFilteredCustomers(unlocated, At("Com coordenada", -23.5510m, -46.6333m));

            var result = await BuildService().GetNearbyAsync(
                EnterpriseId, OriginLatitude, OriginLongitude, radiusKm: 50, take: 50);

            Assert.Equal("Com coordenada", Assert.Single(result).CustomerName);
        }

        [Fact]
        public async Task NearbyByPlace_TreatsBlankAsNoPlaceAtAll()
        {
            var far = In("Longe", "Batel", "Curitiba", "PR", -25.4290m, -49.2710m);

            GivenFilteredCustomers(far);

            var result = await BuildService().GetNearbyAsync(
                EnterpriseId, OriginLatitude, OriginLongitude, radiusKm: 5, take: 50, place: "   ");

            Assert.Empty(result);
        }

        [Fact]
        public async Task Backfill_LocatesWhatItCanAndLeavesTheRestAlone()
        {
            var locatable = At("Com endereço", 0m, 0m, "São Paulo");
            locatable.Latitude = null;
            locatable.Longitude = null;

            var unknown = At("Endereço desconhecido", 0m, 0m, "Xique-Xique");
            unknown.Latitude = null;
            unknown.Longitude = null;

            GivenCustomers(locatable, unknown);
            _customers.Setup(r => r.Update(It.IsAny<Customer>())).Returns((Customer customer) => customer);

            _geocoder.Setup(g => g.ResolveAsync(It.Is<AddressToLocate>(a => a.Municipio == "São Paulo"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GeocodedPoint(-23.550520m, -46.633308m));
            _geocoder.Setup(g => g.ResolveAsync(It.Is<AddressToLocate>(a => a.Municipio == "Xique-Xique"), It.IsAny<CancellationToken>()))
                .ReturnsAsync((GeocodedPoint?)null);

            var result = await BuildService().GeocodePendingAsync(EnterpriseId, take: 25);

            Assert.Equal(2, result.Examined);
            Assert.Equal(1, result.Located);
            Assert.Equal(-23.550520m, locatable.Latitude);
            Assert.Null(unknown.Latitude);
        }

        [Fact]
        public async Task Backfill_CommitsNothingWhenItLocatedNothing()
        {
            var pending = At("Sem sorte", 0m, 0m, "Xique-Xique");
            pending.Latitude = null;
            pending.Longitude = null;

            GivenCustomers(pending);
            _geocoder.Setup(g => g.ResolveAsync(It.IsAny<AddressToLocate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((GeocodedPoint?)null);

            await BuildService().GeocodePendingAsync(EnterpriseId, take: 25);

            _customers.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Backfill_HandsOverTheWholeAddress_NotJustTheCep()
        {
            var customer = At("Menino Deus", 0m, 0m, "Porto Alegre");
            customer.Latitude = null;
            customer.Longitude = null;
            customer.Logradouro = "Rua Barão de Teffe";
            customer.Numero = "273";
            customer.Bairro = "Menino Deus";
            customer.Uf = "RS";
            customer.Cep = "90160150";

            GivenCustomers(customer);
            _customers.Setup(r => r.Update(It.IsAny<Customer>())).Returns((Customer c) => c);

            AddressToLocate asked = default;
            _geocoder.Setup(g => g.ResolveAsync(It.IsAny<AddressToLocate>(), It.IsAny<CancellationToken>()))
                .Callback<AddressToLocate, CancellationToken>((address, _) => asked = address)
                .ReturnsAsync(new GeocodedPoint(-30.057775m, -51.219485m));

            await BuildService().GeocodePendingAsync(EnterpriseId, take: 25);

            Assert.Equal("Rua Barão de Teffe", asked.Logradouro);
            Assert.Equal("273", asked.Numero);
            Assert.Equal("Porto Alegre", asked.Municipio);
            Assert.Equal("RS", asked.Uf);
            Assert.Equal(-30.057775m, customer.Latitude);
        }

        [Fact]
        public async Task SetLocation_MovesTheCustomerAndSaysSo()
        {
            var customer = At("Movido à mão", 0m, 0m, "Porto Alegre");
            GivenFilteredCustomers(customer);
            _customers.Setup(r => r.Update(It.IsAny<Customer>())).Returns((Customer c) => c);

            var moved = await BuildService().SetLocationAsync(EnterpriseId, customer.Id, -30.058100m, -51.219300m);

            Assert.True(moved);
            Assert.Equal(-30.058100m, customer.Latitude);
            Assert.Equal(-51.219300m, customer.Longitude);
            _customers.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SetLocation_WillNotMoveAnotherTenantsCustomer()
        {
            var theirs = At("De outra empresa", 0m, 0m, "Porto Alegre");
            theirs.EnterpriseId = Guid.NewGuid();
            GivenFilteredCustomers(theirs);

            var moved = await BuildService().SetLocationAsync(EnterpriseId, theirs.Id, -30.058100m, -51.219300m);

            Assert.False(moved);
            Assert.Equal(0m, theirs.Latitude);
            _customers.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SetLocation_RefusesAPointOffTheGlobe()
        {
            var customer = At("Fora do mundo", 0m, 0m, "Porto Alegre");
            GivenFilteredCustomers(customer);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => BuildService().SetLocationAsync(EnterpriseId, customer.Id, 91m, 0m));
        }

        [Fact]
        public async Task Backfill_LeavesAHandPlacedPinAlone()
        {
            var placed = At("Fixado à mão", -30.058100m, -51.219300m, "Porto Alegre");

            GivenFilteredCustomers(placed);

            var result = await BuildService().GeocodePendingAsync(EnterpriseId, take: 25);

            Assert.Equal(0, result.Examined);
            _geocoder.Verify(
                g => g.ResolveAsync(It.IsAny<AddressToLocate>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Backfill_SkipsACustomerWithNothingToGeocode()
        {
            var blank = At("Sem endereço", 0m, 0m);
            blank.Latitude = null;
            blank.Longitude = null;

            GivenFilteredCustomers(blank);

            var result = await BuildService().GeocodePendingAsync(EnterpriseId, take: 25);

            Assert.Equal(0, result.Examined);
            _geocoder.Verify(
                g => g.ResolveAsync(It.IsAny<AddressToLocate>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
