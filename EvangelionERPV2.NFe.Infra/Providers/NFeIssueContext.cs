using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Infra.Providers
{
    /// <summary>
    /// Everything a transport needs to build and send one note, including the identity the service
    /// already allocated and committed.
    /// </summary>
    /// <remarks>
    /// The identity fields are inputs, not outputs. The chave, number and series are written to the
    /// database before the first byte leaves precisely so a timed-out request can be reconciled
    /// against them (docs/nfe/HANDOFF.md §4); a transport that invented its own would put the
    /// recovery key back under SEFAZ's control.
    /// </remarks>
    public sealed class NFeIssueContext
    {
        public required Order Order { get; init; }
        public required IReadOnlyList<OrderedProduct> Items { get; init; }
        public required EnterpriseFiscalSettings FiscalSettings { get; init; }
        public Enterprise? Enterprise { get; init; }
        public Customer? Customer { get; init; }

        public required NFeDocumentType Type { get; init; }

        /// <summary>The 44-digit chave allocated and committed before this call.</summary>
        public required string AccessKey { get; init; }

        public required string Number { get; init; }
        public required string Series { get; init; }

        /// <summary>tpAmb: 1 produção, 2 homologação. Taken from the tenant's profile.</summary>
        public byte Environment => FiscalSettings.Environment;

        /// <summary>
        /// tpEmis. 1 = normal, 9 = contingência off-line NFC-e. An input, like the chave: the
        /// service decided it and baked it into the access key before this call.
        /// </summary>
        public byte TipoEmissao { get; init; } = 1;

        /// <summary>dhCont. Required on the wire when <see cref="TipoEmissao"/> is a contingency mode.</summary>
        public DateTime? ContingencyEnteredAt { get; init; }

        /// <summary>xJust. Required on the wire when <see cref="TipoEmissao"/> is a contingency mode.</summary>
        public string? ContingencyJustification { get; init; }

        /// <summary>Offline NFC-e, tpEmis 9 — issued and printed at the counter, transmitted later.</summary>
        public bool IsOfflineContingency => TipoEmissao == 9;

        /// <summary>
        /// The value at or above which an NFCe must identify its consumer. R$ 10.000 is the national
        /// reference.
        /// </summary>
        /// <remarks>
        /// Configuration rather than a constant, deliberately: the figure is **unverified per UF**
        /// (docs/fiscal/TODO-manual.md §M11), so a wrong answer must cost an edit rather than a
        /// release. Below it the dest group is omitted entirely, which is the ordinary counter sale.
        /// </remarks>
        public decimal ConsumerIdentificationThreshold { get; init; } = 10_000m;

        /// <summary>
        /// verProc. Capped at 20 characters by the 4.00 schema — a longer string is rejected by the
        /// XSD, which cost a phase-0 gate run to discover.
        /// </summary>
        public string ProcessVersion { get; init; } = "EvangelionERP-1.0";
    }
}
