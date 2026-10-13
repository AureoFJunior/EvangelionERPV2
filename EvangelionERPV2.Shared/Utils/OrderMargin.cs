using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>
    /// Turns the per-line cost snapshots on an order into the three figures every margin surface
    /// needs. One implementation so the order screen, the order DTO and the TopProductsRevenue
    /// report cannot disagree about what "margin" means.
    /// </summary>
    public static class OrderMargin
    {
        /// <summary>What the goods on this order cost, or null if any line's cost is unknown.</summary>
        public static decimal? TotalCost(IEnumerable<OrderedProduct>? lines)
        {
            if (lines == null)
                return null;

            var lineList = lines.Where(x => x.IsActive != false).ToList();
            if (lineList.Count == 0)
                return null;

            if (lineList.Any(x => !x.UnitCost.HasValue))
                return null;

            return Round(lineList.Sum(x => x.UnitCost!.Value * x.Quantity));
        }

        /// <summary>Revenue minus cost. Negative when the order was sold below cost.</summary>
        public static decimal? Amount(decimal totalValue, decimal? totalCost)
        {
            return totalCost.HasValue ? Round(totalValue - totalCost.Value) : null;
        }

        /// <summary>
        /// Margin over revenue, as a percentage. Null when revenue is zero — a refunded order has a
        /// TotalValue of 0 and dividing by it would answer infinity.
        /// </summary>
        public static decimal? Percent(decimal totalValue, decimal? totalCost)
        {
            if (!totalCost.HasValue || totalValue == 0)
                return null;

            return Round((totalValue - totalCost.Value) / totalValue * 100m);
        }

        /// <summary>Margin of a single unit sold at <paramref name="unitPrice"/>.</summary>
        public static decimal? UnitPercent(decimal unitPrice, decimal? unitCost)
        {
            if (!unitCost.HasValue || unitPrice == 0)
                return null;

            return Round((unitPrice - unitCost.Value) / unitPrice * 100m);
        }

        private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
