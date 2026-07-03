using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.DTOs
{
    public class AuditTrailDTO
    {
        public Guid Id { get; set; }
        public Guid? UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public DateTime ChangedAt { get; set; }
        public string Action { get; set; } = string.Empty;
        public string EntityName { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        [JsonIgnore]
        public string ChangesJson { get; set; } = "{}";
    }
}
