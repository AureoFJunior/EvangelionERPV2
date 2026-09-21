using System.Net;
using System.Text;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.CustomerModule.Application.Services;
using Microsoft.Extensions.Caching.Distributed;
using Moq;

namespace EvangelionERPV2.CustomerModule.Test
{
    public class NominatimAddressGeocoderTests
    {
        /// <summary>Answers every call with one canned body and records what was asked.</summary>
        private sealed class CannedHandler : HttpMessageHandler
        {
            private readonly string _body;
            public string? RequestedUri { get; private set; }

            public CannedHandler(string body) => _body = body;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestedUri = request.RequestUri?.ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                });
            }
        }

        private static (NominatimAddressGeocoder Geocoder, CannedHandler Handler) Build(string body)
        {
            var handler = new CannedHandler(body);
            var client = new HttpClient(handler) { BaseAddress = new Uri("https://nominatim.openstreetmap.org/") };

            var cache = new Mock<IDistributedCache>();
            cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[]?)null);

            return (new NominatimAddressGeocoder(client, cache.Object), handler);
        }

        private static AddressToLocate BaraoDeTeffe => new(
            "Rua Barão de Teffe", "273", "Menino Deus", "Porto Alegre", "RS", "90160150");

        private const string FourRoadSegments = """
        [
          {"lat":"-30.0608356","lon":"-51.2202515","addresstype":"road","name":"Rua Barão de Teffé"},
          {"lat":"-30.0599238","lon":"-51.2200306","addresstype":"road","name":"Rua Barão de Teffé"},
          {"lat":"-30.0577754","lon":"-51.2194851","addresstype":"road","name":"Rua Barão de Teffé"},
          {"lat":"-30.0584940","lon":"-51.2196730","addresstype":"road","name":"Rua Barão de Teffé"}
        ]
        """;

        [Fact]
        public async Task ASplitStreet_LandsInItsMiddle_NotOnWhicheverSegmentRankedFirst()
        {
            var (geocoder, _) = Build(FourRoadSegments);

            var point = await geocoder.ResolveAsync(BaraoDeTeffe);

            Assert.NotNull(point);
            Assert.Equal(-30.0592572m, point!.Value.Latitude, 6);
            Assert.Equal(-51.2198600m, point!.Value.Longitude, 6);
        }

        [Fact]
        public async Task ASurveyedHouseNumber_WinsOutright()
        {
            var (geocoder, _) = Build("""
            [
              {"lat":"-23.5617698","lon":"-46.6553299","addresstype":"building","name":"1578"},
              {"lat":"-23.5600000","lon":"-46.6500000","addresstype":"road","name":"Avenida Paulista"}
            ]
            """);

            var point = await geocoder.ResolveAsync(new AddressToLocate("Avenida Paulista", "1578", null, "São Paulo", "SP", null));

            Assert.Equal(-23.5617698m, point!.Value.Latitude);
        }

        [Fact]
        public async Task TwoDifferentStreets_AreNotAveragedIntoAPointOnNeither()
        {
            var (geocoder, _) = Build("""
            [
              {"lat":"-30.0600000","lon":"-51.2200000","addresstype":"road","name":"Rua Barão de Teffé"},
              {"lat":"-30.0100000","lon":"-51.1800000","addresstype":"road","name":"Rua Outra Coisa"}
            ]
            """);

            var point = await geocoder.ResolveAsync(BaraoDeTeffe);

            Assert.Equal(-30.0600000m, point!.Value.Latitude);
        }

        [Fact]
        public async Task TheCepIsNotSent_BecauseNominatimTreatsItAsAFilter()
        {
            var (geocoder, handler) = Build(FourRoadSegments);

            await geocoder.ResolveAsync(BaraoDeTeffe);

            Assert.DoesNotContain("postalcode", handler.RequestedUri);
            Assert.DoesNotContain("90160150", handler.RequestedUri);
            Assert.Contains("street=273 Rua Barão de Teffe", Uri.UnescapeDataString(handler.RequestedUri!));
            Assert.Contains("city=Porto Alegre", Uri.UnescapeDataString(handler.RequestedUri!));
            Assert.Contains("state=RS", handler.RequestedUri);
        }

        [Fact]
        public async Task NoMunicipio_AsksNothing()
        {
            var (geocoder, handler) = Build(FourRoadSegments);

            var point = await geocoder.ResolveAsync(new AddressToLocate("Rua Sem Cidade", "10", null, null, null, "90160150"));

            Assert.Null(point);
            Assert.Null(handler.RequestedUri);
        }
    }
}
