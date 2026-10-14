using EvangelionERPV2.Shared.Entities;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class BillDTO : BaseDTO
    {
        public Guid OrderId { get; set; }
        public int BankCode { get; set; }
        public string OurNumber { get; set; } = "";
        public string DocumentNumber { get; set; } = "";
        public DateTime IssueDate { get; set; } = DateTime.UtcNow;
        public DateTime DueDate { get; set; } = DateTime.UtcNow;
        public decimal Amount { get; set; } = 0;
        public string DigitableLine { get; set; } = "";
        public string BarCode { get; set; } = "";

        /// <summary>
        /// Carries <see cref="Bill.StatusAsOf"/>, not the stored column, so an open boleto past its
        /// due date reads as Overdue without a sweep job ever having run. See MapperConfig.
        /// </summary>
        public BillStatus Status { get; set; } = BillStatus.Registered;
        public DateTime? PaidAt { get; set; }
        public decimal? PaidAmount { get; set; }
        public string? SettlementProtocol { get; set; }

        [JsonIgnore]
        public string HtmlContent { get; set; } = "";
    }
}
