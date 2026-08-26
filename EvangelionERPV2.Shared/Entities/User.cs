using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(UserName), nameof(Password))]
    // Declared explicitly so ForeignKeyIndexConvention does not drop the FK index from the
    // model: the filtered unique index on (EnterpriseId, NormalizedActiveUserName) only covers
    // IsActive = 1 rows, while tenant FK lookups need all rows. The physical index already
    // exists in the database and the model snapshot.
    [Index(nameof(EnterpriseId))]
    public class User : BaseEntity
    {
        public User() { }

        public User(string firstName, string lastName, string userName, string password, string email, 
            DateTime birthDate, string profilePicture = "", short? isLogged = 0, short actualTheme = 0,
            Guid enterpriseId = default(Guid), short accessLevel = 3)
        {
            FirstName = firstName;
            LastName = lastName;
            UserName = userName;
            Password = password;
            Email = email;
            BirthDate = birthDate;
            IsLogged = isLogged;
            ProfilePicture = profilePicture;
            ActualTheme = actualTheme;
            EnterpriseId = enterpriseId;
            AccessLevel = accessLevel;
        }

        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime BirthDate { get; set; }
        public short? IsLogged { get; set; }
        public short ActualTheme { get; set; }
        public string ProfilePicture { get; set; } = string.Empty;

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; }
        [JsonIgnore]
        public virtual Enterprise? Enterprise { get; set; }
        public short AccessLevel { get; set; }

        /// <summary>
        /// PBKDF2 verifier for this user's till override PIN. Null means they cannot authorise an
        /// override at a terminal, which is the correct default for everyone who never sets one.
        /// </summary>
        /// <remarks>
        /// The verifier — never the PIN — is handed to enrolled terminals so an override works with
        /// the network down. See <see cref="Security.Pbkdf2Verifier"/> for why that is safe.
        /// </remarks>
        [MaxLength(256)]
        public string? PdvPinVerifier { get; set; }

        /// <summary>
        /// Share of what this seller's orders bring in, as a fraction — 0.05 is 5%. Null means no
        /// commission is owed, which is not the same as a rate of zero: a null seller is left off
        /// the commission column entirely rather than shown as earning nothing.
        /// </summary>
        /// <remarks>
        /// decimal(5,4) is mandatory: the global HavePrecision(18,2) convention in AppDbContext
        /// would truncate a 0.0350 rate to 0.04 — the same trap the payroll rates carry.
        /// </remarks>
        [Column(TypeName = "decimal(5,4)")]
        public decimal? CommissionRate { get; set; } = null;
        public short Language { get; set; }
    }
}
