using EvangelionERPV2.Shared.Entities;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class NFeDocumentDTO : BaseDTO
    {
        public Guid OrderId { get; set; }
        public NFeDocumentType Type { get; set; }
        public NFeStatus Status { get; set; }
        public string AccessKey { get; set; } = "";
        public string Series { get; set; } = "";
        public string Number { get; set; } = "";
        public string Environment { get; set; } = "";
        public string Protocol { get; set; } = "";
        public DateTime? IssuedAt { get; set; }
        public double TotalValue { get; set; }
        [JsonIgnore]
        public string XmlContent { get; set; } = "";
        public string CancelReason { get; set; } = "";
        public string CancelProtocol { get; set; } = "";
    }
}
