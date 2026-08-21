namespace EvangelionERPV2.BillsModule.Application.Configs
{
    /// <summary>
    /// The régua de cobrança: when a reminder goes out, relative to the due date.
    /// </summary>
    /// <remarks>
    /// Installation-wide configuration rather than a per-tenant table, because it holds four
    /// integers nobody will change per tenant. What <em>is</em> per tenant is permission to write
    /// to somebody else's customers, and that lives on <c>Enterprise.ShouldSendDunning</c>.
    ///
    /// ponytail: one cadence for the installation. A nullable CSV column on Enterprise is the
    /// upgrade path the day a tenant actually asks for its own.
    /// </remarks>
    public class DunningSettings
    {
        /// <summary>Off by default. Turning it on emails real customers.</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Days relative to the due date. Negative is a courtesy reminder before it falls due,
        /// zero is the day itself, positive is a chase.
        /// </summary>
        public int[] OffsetDays { get; set; } = [-3, 0, 7, 15, 30];

        /// <summary>UTC hour of the daily slot. 11:00 UTC is 08:00 in BRT.</summary>
        public int SweepHourUtc { get; set; } = 11;

        /// <summary>Cap per tenant per pass, so one broken tenant cannot hold the slot forever.</summary>
        public int MaxRemindersPerTenantPerPass { get; set; } = 200;
    }
}
