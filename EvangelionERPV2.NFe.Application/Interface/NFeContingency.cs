namespace EvangelionERPV2.NFeModule.Application.Interface
{
    /// <summary>
    /// The decision to issue an NFC-e off-line, and the two values the schema demands with it.
    /// </summary>
    /// <remarks>
    /// A record rather than a pair of loose parameters because they are meaningless apart: dhCont
    /// without xJust is rejected, and either without tpEmis 9 is a note that lies about how it was
    /// issued. Passing the whole thing or nothing is what keeps them together.
    ///
    /// This is deliberately a per-issuance decision and not a setting. A flag on the tenant would
    /// mean somebody has to remember to turn it off, and every note emitted until they do claims
    /// contingency it did not need.
    /// </remarks>
    public sealed record NFeContingency
    {
        /// <summary>dhCont — when the shop entered contingency, UTC.</summary>
        public required DateTime EnteredAt { get; init; }

        /// <summary>xJust — why. 15 to 256 characters, enforced by the schema and again at build time.</summary>
        public required string Justification { get; init; }
    }
}
