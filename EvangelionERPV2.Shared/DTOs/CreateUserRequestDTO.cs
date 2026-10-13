namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// The wire shape for creating a user. <c>EnterpriseId</c>, <c>AccessLevel</c>, <c>IsLogged</c>
    /// and the audit columns are absent: the controller derives the tenant from the token and every
    /// new user starts as an Employee.
    /// </summary>
    public sealed class CreateUserRequestDTO
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime BirthDate { get; set; }
        public string? ProfilePicture { get; set; }
        public short ActualTheme { get; set; }
        public short Language { get; set; }
    }
}
