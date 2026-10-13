using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
using System.Text.Json;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Margin is a number the company makes decisions with, so the rule that matters is that an
    /// unknown cost never reports as a margin. Every case below is a way that could go wrong.
    /// </summary>
    public class OrderMarginTests
    {
        private static OrderedProduct Line(decimal quantity, decimal value, decimal? unitCost) =>
            new() { Quantity = quantity, Value = value, UnitCost = unitCost, IsActive = true };

        [Fact]
        public void TotalCost_SumsTheLineSnapshots()
        {
            var lines = new[] { Line(2m, 100m, 40m), Line(3m, 50m, 10m) };

            Assert.Equal(110m, OrderMargin.TotalCost(lines));
        }

        [Fact]
        public void TotalCost_IsUnknownWhenAnyLineHasNoCost()
        {
            var lines = new[] { Line(2m, 100m, 40m), Line(3m, 50m, null) };

            Assert.Null(OrderMargin.TotalCost(lines));
        }

        [Fact]
        public void TotalCost_IsUnknownForNoLines()
        {
            Assert.Null(OrderMargin.TotalCost(null));
            Assert.Null(OrderMargin.TotalCost(Array.Empty<OrderedProduct>()));
        }

        [Fact]
        public void TotalCost_IgnoresInactiveLines()
        {
            var lines = new[] { Line(2m, 100m, 40m), new OrderedProduct { Quantity = 5m, UnitCost = null, IsActive = false } };

            Assert.Equal(80m, OrderMargin.TotalCost(lines));
        }

        [Fact]
        public void Amount_AndPercent_FollowTheCost()
        {
            Assert.Equal(120m, OrderMargin.Amount(200m, 80m));
            Assert.Equal(60m, OrderMargin.Percent(200m, 80m));
            Assert.Null(OrderMargin.Amount(200m, null));
            Assert.Null(OrderMargin.Percent(200m, null));
        }

        [Fact]
        public void Amount_IsNegativeWhenSoldBelowCost()
        {
            Assert.Equal(-50m, OrderMargin.Amount(100m, 150m));
            Assert.Equal(-50m, OrderMargin.Percent(100m, 150m));
        }

        [Fact]
        public void Percent_IsUnknownWhenThereIsNoRevenue()
        {
            Assert.Null(OrderMargin.Percent(0m, 80m));
            Assert.Null(OrderMargin.UnitPercent(0m, 8m));
        }

        [Fact]
        public void OrderedProduct_NeverSerializesItsCostSnapshot()
        {
            var json = JsonSerializer.Serialize(Line(2m, 100m, 42.75m));

            Assert.DoesNotContain("42.75", json);
            Assert.DoesNotContain("UnitCost", json);
        }

        [Fact]
        public void OrderDTO_CarriesNoMarginUntilItIsFilled()
        {
            var dto = new OrderDTO { TotalValue = 100m };

            Assert.Null(dto.TotalCost);
            Assert.Null(dto.MarginAmount);
            Assert.Null(dto.MarginPercent);
        }
    }
}
