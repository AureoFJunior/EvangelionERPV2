using EvangelionERPV2.Shared.Entities;

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
        public Enterprise? Enterprise { get; set; }
        public short ActualTheme { get; set; }
        public string Token { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public short? AccessLevel { get; set; }
        public short? Language { get; set; }
    }
}
