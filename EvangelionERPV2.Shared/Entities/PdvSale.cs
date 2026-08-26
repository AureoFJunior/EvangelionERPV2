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
    /// <remarks>
    /// A PDV sale is still an <see cref="Order"/> — that is what every report, receivable, margin
    /// calculation and NFe path already reads, and a second sales ledger would have to be kept
    /// honest against the first forever. This row hangs off that order rather than replacing it.
    ///
    /// <see cref="ClientSaleId"/> is the whole reason the table can be written from an unreliable
    /// network. The terminal mints it before the customer walks away, so the same sale re-sent
    /// after a timeout collides on the unique index and returns the order it already created,
    /// instead of selling the same basket twice. Server-side ids cannot do this: the terminal never
    /// learns them when the response is what got lost.
    /// </remarks>
    // Not filtered on IsActive, and not filtered at all. A soft-deleted row that dropped out of the
    // index would let a retry of that exact sale create a second order — the one failure this table
    // exists to prevent.
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
        /// <remarks>
        /// The money has already been taken out of the line values the order was built from, because
        /// <c>Order.TotalValue</c> is Σ(Value × Quantity) and nothing may make that untrue. Storing
        /// the figure again here is what lets "how much did we discount last month" be a query
        /// rather than a reconstruction from list prices that may since have changed.
        /// </remarks>
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
