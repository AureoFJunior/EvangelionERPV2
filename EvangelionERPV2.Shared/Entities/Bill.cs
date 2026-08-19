using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// Settlement state of a boleto. Persisted, except for <see cref="Overdue"/> — see
    /// <see cref="Bill.StatusAsOf"/>.
    /// </summary>
    public enum BillStatus
    {
        /// <summary>Issued and open. The only state a freshly generated boleto is ever in.</summary>
        Registered = 1,

        /// <summary>Settled. <see cref="Bill.PaidAt"/> and <see cref="Bill.PaidAmount"/> are filled.</summary>
        Paid = 2,

        /// <summary>
        /// Open past its due date. Derived from the clock, never stored — see
        /// <see cref="Bill.StatusAsOf"/>.
        /// </summary>
        Overdue = 3,

        /// <summary>
        /// Superseded by a reissue, or taken off the wallet (baixa). Excluded from the unique
        /// index on <see cref="OrderId"/>, which is what makes a reissue possible at all.
        /// </summary>
        Cancelled = 4,

        /// <summary>
        /// The bank refused the title. Nothing writes this yet — it arrives with CNAB retorno
        /// processing (M9); the value exists now so the retorno mapping does not have to migrate
        /// the column later.
        /// </summary>
        Rejected = 5
    }

    // Uniqueness on OrderId is declared fluently in AppDbContext, not here: it needs a filter on
    // [Status] and the [Index] attribute cannot express one. Without that filter one order gets
    // exactly one boleto forever and a wrong due date is unrecoverable through the API.
    [Index(nameof(CreatedAt), nameof(UpdatedAt), nameof(IsActive), nameof(OrderId))]
    [Table("Boleto")]
    public class Bill : BaseEntity
    {
        public Bill() { }

        public Bill(Guid orderId, int bankCode, string ourNumber, string documentNumber, DateTime issueDate, DateTime dueDate, decimal amount, string digitableLine, string barCode, string htmlContent)
        {
            OrderId = orderId;
            BankCode = bankCode;
            OurNumber = ourNumber;
            DocumentNumber = documentNumber;
            IssueDate = issueDate;
            DueDate = dueDate;
            Amount = amount;
            DigitableLine = digitableLine;
            BarCode = barCode;
            HtmlContent = htmlContent;
        }

        [ForeignKey(nameof(Order))]
        public Guid OrderId { get; set; }

        [JsonIgnore]
        public virtual Order? Order { get; set; }

        public int BankCode { get; set; } = 0;
        public string OurNumber { get; set; } = "";
        public string DocumentNumber { get; set; } = "";
        public DateTime IssueDate { get; set; } = DateTime.UtcNow;
        public DateTime DueDate { get; set; } = DateTime.UtcNow;
        public decimal Amount { get; set; } = 0;
        public string DigitableLine { get; set; } = "";
        public string BarCode { get; set; } = "";
        public string HtmlContent { get; set; } = "";

        public BillStatus Status { get; set; } = BillStatus.Registered;

        /// <summary>When the money actually landed. Null until the boleto is settled.</summary>
        public DateTime? PaidAt { get; set; }

        /// <summary>
        /// What was actually received, which is not always <see cref="Amount"/>: a payer can settle
        /// late with interest or short. Stored separately so the receivable reconciles against the
        /// bank, not against what we hoped to receive.
        /// </summary>
        [Precision(18, 2)]
        public decimal? PaidAmount { get; set; }

        /// <summary>
        /// Whatever identifies the settlement at its source — the bank's authentication for a
        /// registered title, or the operator's reference for a manual settle. Free text because
        /// every bank shapes it differently; it exists so a settlement can be traced back.
        /// </summary>
        [MaxLength(64)]
        public string? SettlementProtocol { get; set; }

        /// <summary>
        /// <see cref="Status"/> as seen at <paramref name="asOfUtc"/>.
        /// </summary>
        /// <remarks>
        /// Overdue is a function of the clock, so storing it would make the column wrong from the
        /// first midnight nobody ran a sweep job. Derived here instead, and the persisted column
        /// keeps saying Registered until something actually settles or cancels the title.
        /// </remarks>
        public BillStatus StatusAsOf(DateTime asOfUtc) =>
            Status == BillStatus.Registered && DueDate.Date < asOfUtc.Date
                ? BillStatus.Overdue
                : Status;
    }
}
