using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One tender against a <see cref="PdvSale"/>. A sale split across cash and PIX has two rows.
    /// </summary>
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
        public Guid? PdvChargeId { get; set; }

        /// <summary>
        /// True when money can be proved to have arrived; false when this is the operator's word.
        /// </summary>
        public bool IsVerified { get; set; }
    }
}
