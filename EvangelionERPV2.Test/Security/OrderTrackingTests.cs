using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Web.FluentValidator;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The tracking link is shown to shoppers as a link on the storefront, so only a web address may
    /// get in — never <c>javascript:</c> or anything else a browser would run (storefront spec E7).
    /// </summary>
    public class OrderTrackingTests
    {
        private static bool Valid(string? code, string? url) => new UpdateOrderRequestValidator()
            .Validate(new UpdateOrderRequestDTO { Id = Guid.NewGuid(), Status = 3, TrackingCode = code, TrackingUrl = url })
            .IsValid;

        [Theory]
        [InlineData("BR123456789BR", "https://rastreamento.correios.com.br/app/index.php?objeto=BR123456789BR", true)]
        [InlineData("BR123456789BR", "http://transportadora.example/rastreio/1", true)]
        [InlineData("BR123456789BR", null, true)]
        [InlineData(null, null, true)]
        [InlineData("BR123456789BR", "javascript:alert(1)", false)]
        [InlineData("BR123456789BR", "ftp://example.com/track", false)]
        [InlineData("BR123456789BR", "/rastreio/1", false)]
        public void TrackingUrl_MustBeAWebAddress(string? code, string? url, bool expected)
        {
            Assert.Equal(expected, Valid(code, url));
        }

        [Fact]
        public void TrackingCode_IsAtMostFortyCharacters_AndTheLinkAtMostFiveHundred()
        {
            Assert.True(Valid(new string('A', 40), null));
            Assert.False(Valid(new string('A', 41), null));
            Assert.False(Valid("BR1", "https://example.com/" + new string('a', 481)));
        }

        [Fact]
        public void OrderDto_CarriesTrackingAndWithdrawal()
        {
            var requestedAt = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
            var dto = MapperConfig.RegisterMaps().CreateMapper().Map<OrderDTO>(new Order
            {
                TrackingCode = "BR123456789BR",
                TrackingUrl = "https://example.com/track/BR123456789BR",
                WithdrawalRequestedAt = requestedAt
            });

            Assert.Equal("BR123456789BR", dto.TrackingCode);
            Assert.Equal("https://example.com/track/BR123456789BR", dto.TrackingUrl);
            Assert.Equal(requestedAt, dto.WithdrawalRequestedAt);
        }
    }
}
