namespace EvangelionERPV2.Domain.DTOs
{
    public class UserDTO
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public DateTime BirthDate { get; set; }
        public string? ProfilePicture { get; set; }
        public string Token { get; set; }
        public string? RefreshToken { get; set; }
    }
}