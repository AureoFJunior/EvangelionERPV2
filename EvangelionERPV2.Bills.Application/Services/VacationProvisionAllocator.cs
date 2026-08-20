using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// Spreads what a férias payment costs across the competências that accrued the provision for
    /// it, oldest month first.
    /// </summary>
    /// <remarks>
    /// Pure, for the same reason <c>VacationEntitlement</c> is: this is the arithmetic somebody
    /// will one day have to defend line by line against a payroll report, and it should be
    /// checkable without a database.
    ///
    /// It computes the whole allocation from a total rather than applying a delta. That is what
    /// makes re-running it safe — see <see cref="VacationProvisionDrawdownService"/>.
    /// </remarks>
    public static class VacationProvisionAllocator
    {
        /// <summary>
        /// Allocates <paramref name="paidTotal"/> across <paramref name="competencias"/>, filling
        /// each month's provision in chronological order before moving to the next.
        /// </summary>
        /// <remarks>
        /// Oldest first is not a preference. The provision accrued month by month, so consuming it
        /// in any other order leaves a remaining balance that cannot be read as "what is still set
        /// aside from the months already worked".
        /// </remarks>
        public static ProvisionDrawdown Allocate(decimal paidTotal, IReadOnlyList<PayrollBillDetail> competencias)
        {
            ArgumentNullException.ThrowIfNull(competencias);

            var remaining = paidTotal > 0 ? paidTotal : 0m;
            var allocations = new List<ProvisionAllocation>(competencias.Count);

            foreach (var detail in competencias
                .OrderBy(x => x.ReferenceYear)
                .ThenBy(x => x.ReferenceMonth))
            {
                // A month can never give back more than it accrued, and never less than nothing —
                // a hand-edited row with a negative provision must not turn into a credit here.
                var available = Math.Max(0m, detail.VacationProvision);
                var consumed = Math.Min(available, remaining);

                allocations.Add(new ProvisionAllocation(detail.Id, consumed));
                remaining -= consumed;
            }

            return new ProvisionDrawdown(allocations, remaining);
        }
    }

    /// <summary>How much of one competência's provision a payment consumed.</summary>
    public readonly record struct ProvisionAllocation(Guid PayrollBillDetailId, decimal Consumed);

    /// <param name="Allocations">One entry per competência considered, including the untouched ones.</param>
    /// <param name="Shortfall">
    /// What the payment cost beyond everything that had been set aside for it.
    /// </param>
    /// <remarks>
    /// A shortfall is ordinary, not an error: an employee hired part-way through the período
    /// aquisitivo accrued fewer than twelve months of it, and a raise since means the provision was
    /// laid down against a smaller salary than the one being paid out. The cash is owed either way,
    /// so the bill stands and the difference is simply not covered by a provision.
    /// </remarks>
    public sealed record ProvisionDrawdown(IReadOnlyList<ProvisionAllocation> Allocations, decimal Shortfall)
    {
        public decimal TotalConsumed => Allocations.Sum(x => x.Consumed);
    }
}
