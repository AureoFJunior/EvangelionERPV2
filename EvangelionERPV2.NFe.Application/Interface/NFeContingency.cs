namespace EvangelionERPV2.NFeModule.Application.Interface
{
    /// <summary>
    /// The decision to issue an NFC-e off-line, and the two values the schema demands with it.
    /// </summary>
    public sealed record NFeContingency
    {
        /// <summary>dhCont — when the shop entered contingency, UTC.</summary>
        public required DateTime EnteredAt { get; init; }

        /// <summary>xJust — why. 15 to 256 characters, enforced by the schema and again at build time.</summary>
        public required string Justification { get; init; }
    }
}
