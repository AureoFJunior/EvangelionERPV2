using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.Shared.DTOs
{
    public class ReportListItemDTO
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public EnumReportType Type { get; set; }
        public string Icon { get; set; } = string.Empty;
    }
}
