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

        /// <summary>
        /// A contact number the user maintains themselves, in whatever shape they type it.
        /// </summary>
        /// <remarks>
        /// Free text, like every other phone column here (Customer, Supplier, Employee). It is a
        /// number somebody rings, not a key: normalising it would fight with extensions, country
        /// prefixes and the "ramal 22" people actually enter. Empty by default — nobody has one
        /// until they fill it in — and 30 characters, the same ceiling the other four use.
        /// </remarks>
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

        /// <summary>
        /// A login that belongs to a process, not to a person — the order worker's account, a seed
        /// account, anything that authenticates without anybody sitting behind it.
        /// </summary>
        /// <remarks>
        /// Exists because nothing else in this table separates the two. The order worker's account
        /// is <see cref="Enums.EnumAccessLevel.Admin"/> like a director is, and its orders were
        /// landing in Desempenho de vendas next to the people who actually sold something. Naming
        /// conventions were the alternative and they break on the first rename.
        ///
        /// False for everybody by default, which is the safe direction: a person wrongly hidden
        /// from their own commission row is a worse failure than a robot briefly listed.
        /// </remarks>
        public bool IsServiceAccount { get; set; } = false;
        public short Language { get; set; }
    }
}
