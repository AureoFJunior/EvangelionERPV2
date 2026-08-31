using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One tender against a <see cref="PdvSale"/>. A sale split across cash and PIX has two rows.
    /// </summary>
    /// <remarks>
    /// Split payment is the normal case at a till, not an edge case, which is why this is a table
    /// and not a pair of columns on the sale. It is also the source NFC-e's <c>pag</c> group is
    /// built from, so the shape has to survive contact with the fiscal schema.
    /// </remarks>
    [Index(nameof(PdvSaleId))]
    [Table("PdvSalePayment")]
    public class PdvSalePayment : BaseEntity
    {
        [ForeignKey(nameof(PdvSale))]
        public Guid PdvSaleId { get; set; }

        [JsonIgnore]
        public virtual PdvSale? PdvSale { get; set; }

        public Guid EnterpriseId { get; set; }

        /// <summary><see cref="Enums.EnumPaymentMethod"/>.</summary>
        public int Method { get; set; }

        /// <summary>
        /// What this tender settled — never more than the sale still owes.
        /// </summary>
        /// <remarks>
        /// For cash this is the amount applied to the sale, not what the customer handed over. A
        /// R$ 50 note against a R$ 43 basket is Amount 43 and <see cref="ChangeGiven"/> 7, so the
        /// tenders sum to the order total exactly and the sum can be asserted.
        /// </remarks>
        public decimal Amount { get; set; }

        /// <summary>Change handed back. Non-zero only for cash.</summary>
        public decimal ChangeGiven { get; set; }

        /// <summary>
        /// Card or PIX authorisation, typed by the operator until a real TEF integration fills it.
        /// Not a secret — it is the reference printed on the customer's own receipt — but it is
        /// still the only handle on a disputed charge, so it is kept verbatim.
        /// </summary>
        [MaxLength(64)]
        public string? AuthorizationCode { get; set; }

        /// <summary>Credit installments. Null or 1 means a single charge.</summary>
        public int? Installments { get; set; }

        /// <summary>
        /// The <c>PdvCharge</c> this tender was settled by, when there was one.
        /// </summary>
        /// <remarks>
        /// Deliberately a bare Guid rather than a navigation property. A charge is created by one
        /// request and moved to Paid by a webhook from a third party; the sale that references it
        /// arrives from a terminal that may have been offline for a week. Tying the sale's write to
        /// the charge row's existence would make a network hiccup at the PSP into a rejected sale.
        /// </remarks>
        public Guid? PdvChargeId { get; set; }

        /// <summary>
        /// True when money can be proved to have arrived; false when this is the operator's word.
        /// </summary>
        /// <remarks>
        /// False is the honest default and the historical truth: every row written before this
        /// column existed was an assertion. True is set only where the ERP itself saw a settled
        /// charge — never from a flag a terminal chose for itself.
        /// </remarks>
        public bool IsVerified { get; set; }
    }
}
