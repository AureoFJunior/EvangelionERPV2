using EvangelionERPV2.Shared.Entities;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Display status. Derived, never persisted: <see cref="NFeStatus"/> stores six values and
    /// the extra distinctions come from the transmission timestamps, so there is one source of
    /// truth and no data migration when the UI wants a new label.
    /// </summary>
    public enum NFeDisplayStatus
    {
        /// <summary>Claimed, not yet transmitted.</summary>
        Processing = 1,

        /// <summary>Transmitted, outcome unknown. Resolved by consulting SEFAZ, never by resending.</summary>
        AwaitingSefaz = 2,

        Authorized = 3,

        /// <summary>Cancellation event sent, homologation not yet confirmed.</summary>
        CancellationRequested = 4,

        Cancelled = 5,

        /// <summary>SEFAZ refused. Correct the data and re-issue with a new number.</summary>
        Rejected = 6,

        /// <summary>Denegada. Terminal — no cancellation, no re-issue.</summary>
        Denied = 7,

        /// <summary>Our failure, before any certainty of transmission. Retryable.</summary>
        Error = 8
    }

    public sealed class NFeDocumentDTO : BaseDTO
    {
        public Guid OrderId { get; set; }
        public Guid EnterpriseId { get; set; }
        public NFeDocumentType Type { get; set; }
        public NFeStatus Status { get; set; }

        /// <summary>What the UI badges. See <see cref="NFeDisplayStatus"/>.</summary>
        public NFeDisplayStatus DisplayStatus { get; set; }

        public short Model { get; set; }
        public string AccessKey { get; set; } = "";
        public string Series { get; set; } = "";
        public string Number { get; set; } = "";
        public string Environment { get; set; } = "";
        public string Protocol { get; set; } = "";
        public DateTime? IssuedAt { get; set; }
        public DateTime? AuthorizedAtUtc { get; set; }
        public decimal TotalValue { get; set; }

        /// <summary>cStat and its reason, so a rejection says why instead of failing silently.</summary>
        public string Cstat { get; set; } = "";
        public string XMotivo { get; set; } = "";

        public string CancelReason { get; set; } = "";
        public string CancelProtocol { get; set; } = "";
        public DateTime? CancelledAtUtc { get; set; }
        public string CancelCstat { get; set; } = "";
        public string CancelXMotivo { get; set; } = "";

        /// <summary>
        /// The authorized XML contains the customer's CPF/CNPJ, full name and full address. It
        /// never travels in a JSON payload — only as a dedicated binary download behind
        /// nfe.xml.download, which sits a tier above the DANFE for exactly this reason.
        /// </summary>
        [JsonIgnore]
        public string XmlContent { get; set; } = "";

        /// <summary>Maps persisted state plus timestamps onto the label the UI shows.</summary>
        public static NFeDisplayStatus ResolveDisplayStatus(NFeDocument document)
        {
            return document.Status switch
            {
                NFeStatus.Authorized when document.CancelRequestedAt != null && document.CancelledAtUtc == null
                    => NFeDisplayStatus.CancellationRequested,
                NFeStatus.Authorized => NFeDisplayStatus.Authorized,
                NFeStatus.Cancelled => NFeDisplayStatus.Cancelled,
                NFeStatus.Rejected => NFeDisplayStatus.Rejected,
                NFeStatus.Denied => NFeDisplayStatus.Denied,
                NFeStatus.Error => NFeDisplayStatus.Error,
                NFeStatus.Pending when document.TransmittedAtUtc != null => NFeDisplayStatus.AwaitingSefaz,
                _ => NFeDisplayStatus.Processing
            };
        }
    }
}
