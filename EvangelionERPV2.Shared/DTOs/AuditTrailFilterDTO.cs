namespace EvangelionERPV2.Shared.DTOs
{
    public class AuditTrailFilterDTO
    {
        public Guid? UserId { get; set; }
        public string? UserName { get; set; }
        public string? EntityName { get; set; }
        public Guid? EntityId { get; set; }
        public string? Action { get; set; }
        public DateTime? ChangedFrom { get; set; }
        public DateTime? ChangedTo { get; set; }
    }
}
