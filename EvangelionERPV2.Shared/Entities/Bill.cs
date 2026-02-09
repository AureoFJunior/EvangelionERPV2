using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(OrderId))]
    [Index(nameof(CreatedAt), nameof(UpdatedAt), nameof(IsActive), nameof(OrderId))]
    [Table("Boleto")]
    public class Bill : BaseEntity
    {
        public Bill() { }

        public Bill(Guid orderId, int bankCode, string ourNumber, string documentNumber, DateTime issueDate, DateTime dueDate, double amount, string digitableLine, string barCode, string htmlContent)
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
        public double Amount { get; set; } = 0;
        public string DigitableLine { get; set; } = "";
        public string BarCode { get; set; } = "";
        public string HtmlContent { get; set; } = "";
    }
}
