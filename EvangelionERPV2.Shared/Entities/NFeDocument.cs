using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    public enum NFeDocumentType
    {
        NFe = 1,
        NFCe = 2
    }

    public enum NFeStatus
    {
        /// <summary>
        /// Claimed. Whether it reached SEFAZ is told by <see cref="NFeDocument.TransmittedAtUtc"/>,
        /// not by this value — see the remarks on the entity.
        /// </summary>
        Pending = 1,

        /// <summary>cStat 100 (or 150, authorized out of time).</summary>
        Authorized = 2,

        /// <summary>Event 110111 homologated: cStat 135 or 155.</summary>
        Cancelled = 3,

        /// <summary>
        /// Our failure — no certificate, bad settings, network, unhandled exception. Retryable.
        /// Never used for a SEFAZ refusal: that is Rejected.
        /// </summary>
        Error = 4,

        /// <summary>
        /// SEFAZ refused. The number is burned; correcting the data and re-issuing takes a NEW
        /// number. Appended rather than inserted so existing wire values stay stable.
        /// </summary>
        Rejected = 5,

        /// <summary>
        /// Denegada — cStat 110/301/302/303. Terminal: the key is burned, it cannot be cancelled
        /// and it cannot be reused.
        /// </summary>
        Denied = 6
    }

    [Index(nameof(OrderId), nameof(Type), IsUnique = true)]
    [Index(nameof(OrderId))]
    [Index(nameof(AccessKey))]
    [Index(nameof(EnterpriseId))]
    [Index(nameof(CreatedAt), nameof(UpdatedAt), nameof(IsActive), nameof(OrderId))]
    public class NFeDocument : BaseEntity
    {
        public NFeDocument() { }

        public NFeDocument(Guid orderId, NFeDocumentType type, NFeStatus status, string accessKey)
        {
            OrderId = orderId;
            Type = type;
            Status = status;
            AccessKey = accessKey;
        }

        [ForeignKey(nameof(Order))]
        public Guid OrderId { get; set; }

        [JsonIgnore]
        public virtual Order? Order { get; set; }

        public NFeDocumentType Type { get; set; } = NFeDocumentType.NFe;
        public NFeStatus Status { get; set; } = NFeStatus.Pending;
        public string AccessKey { get; set; } = "";
        public string Series { get; set; } = "";
        public string Number { get; set; } = "";
        public string Environment { get; set; } = "";
        public string Protocol { get; set; } = "";
        public DateTime? IssuedAt { get; set; }
        public decimal TotalValue { get; set; } = 0;
        public string XmlContent { get; set; } = "";
        public string CancelReason { get; set; } = "";
        public string CancelProtocol { get; set; } = "";

        /// <summary>
        /// Denormalised from Order.EnterpriseId. Tenant scoping used to walk the Order on three
        /// separate query paths, which made a tenant-scoped list impossible without a join.
        /// The Order-based check is kept as defence in depth on the issue path.
        /// </summary>
        /// <remarks>
        /// Deliberately a plain indexed column with no navigation property and no foreign key.
        /// A relationship to Enterprise would introduce a delete cascade onto fiscal documents,
        /// and an authorized NFe must be retained for five years (Ajuste SINIEF 07/2005) — it
        /// may not disappear because a tenant row was removed. The Order FK already carries the
        /// referential relationship.
        /// </remarks>
        public Guid EnterpriseId { get; set; }

        /// <summary>55 = NFe, 65 = NFCe. <see cref="Type"/> stays as the API-facing enum.</summary>
        public short Model { get; set; }

        #region SEFAZ outcome

        /// <summary>cStat as returned: "100", "204", "539", "110".</summary>
        [MaxLength(3)]
        public string Cstat { get; set; } = "";

        [MaxLength(255)]
        public string XMotivo { get; set; } = "";

        /// <summary>nRec from an asynchronous batch, needed to recover via RetAutorizacao.</summary>
        [MaxLength(15)]
        public string ReceiptNumber { get; set; } = "";

        /// <summary>
        /// Write-ahead marker, and the single most important column here.
        /// NULL means the document provably never left this process, so retransmitting the same
        /// number is safe. NOT NULL with status Pending means the outcome is UNKNOWN: the note
        /// may be authorized at SEFAZ. Retransmitting in that state double-emits, with real tax
        /// liability. It must be resolved by consulting SEFAZ, never by resending.
        /// </summary>
        public DateTime? TransmittedAtUtc { get; set; }

        /// <summary>dhRecbto from protNFe. The clock the cancellation window is measured from.</summary>
        public DateTime? AuthorizedAtUtc { get; set; }

        [MaxLength(64)]
        public string DigestValue { get; set; } = "";

        /// <summary>Reconciliation backoff counter.</summary>
        public int AttemptCount { get; set; }

        #endregion

        #region Cancellation

        /// <summary>Set before the 110111 round trip — same unknown-outcome trap as issuance.</summary>
        public DateTime? CancelRequestedAt { get; set; }

        public DateTime? CancelledAtUtc { get; set; }

        [MaxLength(3)]
        public string CancelCstat { get; set; } = "";

        [MaxLength(255)]
        public string CancelXMotivo { get; set; } = "";

        #endregion

        #region NFCe

        // Both embed the access key, which carries the emitente CNPJ and the note number.
        // Neither is exposed on the DTO, and both are redacted in the audit trail.

        [MaxLength(1000)]
        public string QrCode { get; set; } = "";

        [MaxLength(500)]
        public string UrlChave { get; set; } = "";

        #endregion

        /// <summary>
        /// True when the document has been transmitted but no outcome is known. Such a document
        /// must be resolved by consulting SEFAZ; retransmitting it risks a duplicate emission.
        /// </summary>
        [NotMapped]
        [JsonIgnore]
        public bool IsOutcomeUnknown => Status == NFeStatus.Pending && TransmittedAtUtc != null;

        /// <summary>Terminal states have no outgoing transition.</summary>
        [NotMapped]
        [JsonIgnore]
        public bool IsTerminal => Status is NFeStatus.Cancelled or NFeStatus.Denied;

        /// <summary>SEFAZ document model for a type: NFe is 55, NFCe is 65.</summary>
        public static short ModelFor(NFeDocumentType type) =>
            type == NFeDocumentType.NFCe ? (short)65 : (short)55;
    }
}
