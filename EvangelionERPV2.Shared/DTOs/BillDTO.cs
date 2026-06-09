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
        public double Amount { get; set; } = 0;
        public string DigitableLine { get; set; } = "";
        public string BarCode { get; set; } = "";
        [JsonIgnore]
        public string HtmlContent { get; set; } = "";
    }
}
