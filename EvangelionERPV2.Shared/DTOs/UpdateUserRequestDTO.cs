namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// The wire shape for updating a user. <c>AccessLevel</c> is here because changing a role is a
    /// real operation behind <c>users.update</c>; the tenant, the login state and the audit columns
    /// are not, and are carried over from the stored row.
    /// </summary>
    public sealed class UpdateUserRequestDTO
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime BirthDate { get; set; }
        public short AccessLevel { get; set; }
        public short ActualTheme { get; set; }
        public short Language { get; set; }

        /// <summary>
        /// Commission as a fraction — 0.05 is 5%. Sent explicitly on every update: unlike the
        /// password, a null here means "no commission", which is a real setting a manager may want.
        /// </summary>
        public decimal? CommissionRate { get; set; }

        /// <summary>Left blank to keep the stored password.</summary>
        public string? Password { get; set; }

        /// <summary>Left blank to keep the stored picture.</summary>
        public string? ProfilePicture { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
