using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Application.Models
{
    public sealed class NFeProviderResult
    {
        public string AccessKey { get; set; } = "";
        public string Protocol { get; set; } = "";
        public string XmlContent { get; set; } = "";
        public string Number { get; set; } = "";
        public string Series { get; set; } = "";
        public string Environment { get; set; } = "";
        public DateTime? IssuedAt { get; set; }
        public double TotalValue { get; set; }
        public NFeStatus Status { get; set; } = NFeStatus.Pending;
    }
}
