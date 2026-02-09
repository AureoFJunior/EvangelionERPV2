using Microsoft.EntityFrameworkCore;
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
        Pending = 1,
        Authorized = 2,
        Cancelled = 3,
        Error = 4
    }

    [Index(nameof(OrderId))]
    [Index(nameof(AccessKey))]
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
        public double TotalValue { get; set; } = 0;
        public string XmlContent { get; set; } = "";
        public string CancelReason { get; set; } = "";
        public string CancelProtocol { get; set; } = "";
    }
}
