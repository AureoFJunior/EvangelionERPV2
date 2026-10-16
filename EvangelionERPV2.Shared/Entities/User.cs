using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(UserName), nameof(Password))]
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

        /// <summary>
        /// A contact number the user maintains themselves, in whatever shape they type it.
        /// </summary>
        [MaxLength(30)]
        public string PhoneNumber { get; set; } = string.Empty;

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; }
        [JsonIgnore]
        public virtual Enterprise? Enterprise { get; set; }
        public short AccessLevel { get; set; }

        /// <summary>
        /// PBKDF2 verifier for this user's till override PIN. Null means they cannot authorise an
        /// override at a terminal, which is the correct default for everyone who never sets one.
        /// </summary>
        [MaxLength(256)]
        public string? PdvPinVerifier { get; set; }

        /// <summary>
        /// Share of what this seller's orders bring in, as a fraction — 0.05 is 5%. Null means no
        /// commission is owed, which is not the same as a rate of zero: a null seller is left off
        /// the commission column entirely rather than shown as earning nothing.
        /// </summary>
        [Column(TypeName = "decimal(5,4)")]
        public decimal? CommissionRate { get; set; } = null;

        /// <summary>
        /// A login that belongs to a process, not to a person — the order worker's account, a seed
        /// account, anything that authenticates without anybody sitting behind it.
        /// </summary>
        public bool IsServiceAccount { get; set; } = false;
        public short Language { get; set; }

        public bool MfaEnabled { get; set; }

        /// <summary>Requires the user to choose a personal password at next sign-in.</summary>
        public bool MustChangePassword { get; set; }
        public int IamVersion { get; set; }

        /// <summary>Encrypted base32 TOTP secret, set only while <see cref="MfaEnabled"/> is true.</summary>
        [MaxLength(256)]
        [JsonIgnore]
        public string? MfaSecret { get; set; }

        /// <summary>Encrypted secret handed out by setup and not yet proven with a code.</summary>
        [MaxLength(256)]
        [JsonIgnore]
        public string? MfaPendingSecret { get; set; }

        /// <summary>Last TOTP step accepted, so a code cannot be used twice.</summary>
        public long? MfaLastUsedStep { get; set; }

        /// <summary>Apple's stable user id (<c>sub</c>) once this account signed in with or linked Apple.</summary>
        [MaxLength(64)]
        [JsonIgnore]
        public string? AppleSubject { get; set; }
    }
}
