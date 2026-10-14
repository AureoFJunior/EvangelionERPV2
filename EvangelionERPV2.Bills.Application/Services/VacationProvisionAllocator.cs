using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// Spreads what a férias payment costs across the competências that accrued the provision for
    /// it, oldest month first.
    /// </summary>
    public static class VacationProvisionAllocator
    {
        /// <summary>
        /// Allocates <paramref name="paidTotal"/> across <paramref name="competencias"/>, filling
        /// each month's provision in chronological order before moving to the next.
        /// </summary>
        public static ProvisionDrawdown Allocate(decimal paidTotal, IReadOnlyList<PayrollBillDetail> competencias)
        {
            ArgumentNullException.ThrowIfNull(competencias);

            var buckets = competencias
                .OrderBy(x => x.ReferenceYear)
                .ThenBy(x => x.ReferenceMonth)
                .Select(x => (x.Id, x.VacationProvision))
                .ToList();

            return Allocate(paidTotal, buckets);
        }

        /// <summary>
        /// The same fill against arbitrary buckets, already in the order they must be consumed.
        /// </summary>
        public static ProvisionDrawdown Allocate(decimal total, IReadOnlyList<(Guid Id, decimal Capacity)> buckets)
        {
            ArgumentNullException.ThrowIfNull(buckets);

            var remaining = total > 0 ? total : 0m;
            var allocations = new List<ProvisionAllocation>(buckets.Count);

            foreach (var (id, capacity) in buckets)
            {
                var available = Math.Max(0m, capacity);
                var consumed = Math.Min(available, remaining);

                allocations.Add(new ProvisionAllocation(id, consumed));
                remaining -= consumed;
            }

            return new ProvisionDrawdown(allocations, remaining);
        }
    }

    /// <summary>
    /// How much of one bucket a fill consumed — a competência giving up its provision, or a férias
    /// bill giving up the share of it that nets its amount down.
    /// </summary>
    public readonly record struct ProvisionAllocation(Guid Id, decimal Consumed);

    /// <param name="Allocations">One entry per competência considered, including the untouched ones.</param>
    /// <param name="Shortfall">
    /// What the payment cost beyond everything that had been set aside for it.
    /// </param>
    public sealed record ProvisionDrawdown(IReadOnlyList<ProvisionAllocation> Allocations, decimal Shortfall)
    {
        public decimal TotalConsumed => Allocations.Sum(x => x.Consumed);
    }
}
