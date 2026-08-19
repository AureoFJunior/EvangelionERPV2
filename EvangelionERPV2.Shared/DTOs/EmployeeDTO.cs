using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class EmployeeDTO : BaseDTO
    {
        public string Name { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;

        // LGPD: identity document and salary are accepted on write but never
        // serialized back in API responses.
        [JsonIgnore]
        public string? Document { get; set; }
        [JsonIgnore]
        public decimal? Salary { get; set; }

        // Optional. Hydrated with the base64 image content by the controller;
        // the stored (encrypted) S3 key is never serialized.
        public string? Picture { get; set; }

        public DateTime HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }
        public Guid? EnterpriseId { get; set; } = null;
    }
}
