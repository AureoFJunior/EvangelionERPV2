using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// The till-side facts about a sale that <see cref="Order"/> has no room for: which session and
    /// terminal rang it, how it was tendered, what discount the operator gave, and the client id
    /// that makes re-sending it safe.
    /// </summary>
    [Index(nameof(EnterpriseId), nameof(ClientSaleId), IsUnique = true)]
    [Index(nameof(CashSessionId))]
    [Table("PdvSale")]
    public class PdvSale : BaseEntity
    {
        public Guid EnterpriseId { get; set; }

        /// <summary>Minted by the terminal, before the sale is finalised and before it is ever sent.</summary>
        public Guid ClientSaleId { get; set; }

        /// <summary>
        /// The order this sale became. Null only inside the sync transaction, or on a row that was
        /// rejected — see <see cref="Enums.EnumPdvSaleStatus"/>.
        /// </summary>
        [ForeignKey(nameof(Order))]
        public Guid? OrderId { get; set; }

        [JsonIgnore]
        public virtual Order? Order { get; set; }

        [ForeignKey(nameof(CashSession))]
        public Guid CashSessionId { get; set; }

        [JsonIgnore]
        public virtual CashSession? CashSession { get; set; }

        [MaxLength(64)]
        public string TerminalId { get; set; } = string.Empty;

        /// <summary>Who rang it. May differ from the session opener across a shift change.</summary>
        public Guid UserId { get; set; }

        /// <summary>
        /// The headline discount the operator granted, for reporting only.
        /// </summary>
        public decimal DiscountAmount { get; set; }

        /// <summary>Interest or fee added on top, same reporting-only reasoning as the discount.</summary>
        public decimal SurchargeAmount { get; set; }

        /// <summary>
        /// When the customer actually paid, per the terminal clock, in UTC. This is the fiscal and
        /// commercial date of the sale; <see cref="BaseEntity.CreatedAt"/> is merely when the server
        /// found out, which for an offline sale can be hours or days later.
        /// </summary>
        public DateTime SoldAtLocal { get; set; }

        /// <summary>When the server accepted it. Null on a row still inside its sync transaction.</summary>
        public DateTime? SyncedAt { get; set; }

        /// <summary><see cref="Enums.EnumPdvSaleStatus"/>.</summary>
        public int Status { get; set; }

        /// <summary>Why it was refused, when it was. Surfaced to the operator, so it is a sentence not a code.</summary>
        [MaxLength(400)]
        public string? RejectReason { get; set; }

        [JsonIgnore]
        public virtual ICollection<PdvSalePayment>? Payments { get; set; }
    }
}
