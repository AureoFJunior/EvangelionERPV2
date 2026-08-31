namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// What became of one attempt to take money through a payment provider.
    /// </summary>
    /// <remarks>
    /// Values are explicit because they are persisted in <c>PdvCharge.Status</c>: reordering the
    /// members must never silently reinterpret rows already written.
    ///
    /// Everything but <see cref="Pending"/> is terminal, and a terminal status never changes again.
    /// That rule is half of the webhook's idempotency — see <c>PdvCharge.ProviderEventId</c>.
    /// </remarks>
    public enum EnumPdvChargeStatus
    {
        /// <summary>Raised with the provider, not yet paid. The only status a till waits on.</summary>
        Pending = 0,

        /// <summary>The bank confirmed the money arrived. The only status a tender may point at.</summary>
        Paid = 1,

        /// <summary>The window closed unpaid. The customer pays another way.</summary>
        Expired = 2,

        /// <summary>Abandoned at the till before anyone paid.</summary>
        Cancelled = 3,

        /// <summary>The provider refused it. Distinct from expiry: nobody waited this one out.</summary>
        Failed = 4
    }
}
