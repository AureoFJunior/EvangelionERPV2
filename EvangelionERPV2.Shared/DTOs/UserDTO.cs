using EvangelionERPV2.Shared.Entities;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class UserDTO
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime BirthDate { get; set; }
        public string? ProfilePicture { get; set; }
        [JsonIgnore]
        public Enterprise? Enterprise { get; set; }
        public short ActualTheme { get; set; }
        public string Token { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public short? AccessLevel { get; set; }
        public short? Language { get; set; }

        /// <summary>
        /// Commission as a fraction — 0.05 is 5%. Null means this seller earns no commission, which
        /// the performance screen shows as a blank cell rather than as zero.
        /// </summary>
        public decimal? CommissionRate { get; set; }
    }
}
