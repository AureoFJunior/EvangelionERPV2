using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(CreatedAt), nameof(UpdatedAt), nameof(IsActive), nameof(Name))]
    [Index(nameof(Name))]
    public class Customer : BaseEntity
    {
        public Customer() { }

        public Customer(string name, string phoneNumber, string email, string adress, string? document = null)
        {
            Name = name;
            PhoneNumber = phoneNumber;
            Email = email;
            Adress = adress;
            Document = document;
        }

        public string Name { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string Email { get; set; } = "";
        public string Adress { get; set; } = "";
        public string? Document { get; set; } = null;

        /// <summary>
        /// How much this customer may owe at once. Null means no limit — the default, and what
        /// every existing customer keeps.
        /// </summary>
        public decimal? CreditLimit { get; set; } = null;

        #region Fiscal address

        [MaxLength(60)]
        public string? Logradouro { get; set; }

        [MaxLength(60)]
        public string? Numero { get; set; }

        [MaxLength(60)]
        public string? Complemento { get; set; }

        [MaxLength(60)]
        public string? Bairro { get; set; }

        /// <summary>cMun — 7-digit IBGE municipality code.</summary>
        [MaxLength(7)]
        public string? CodigoMunicipioIbge { get; set; }

        [MaxLength(60)]
        public string? Municipio { get; set; }

        /// <summary>v1 allowlist: SP, RS, SC, PR.</summary>
        [MaxLength(2)]
        public string? Uf { get; set; }

        [MaxLength(8)]
        public string? Cep { get; set; }

        /// <summary>
        /// Decides indIEDest: a customer with an IE is ContribuinteICMS, one without is
        /// NaoContribuinte. Null is the ordinary case — most customers are not ICMS contributors.
        /// </summary>
        [MaxLength(14)]
        public string? InscricaoEstadual { get; set; }

        #endregion

        /// <summary>
        /// Where this customer is, for the map. Resolved from the fiscal address above by the
        /// geocoding backfill — nothing on the write path calls out to the internet, so saving a
        /// customer never waits on a third party or fails because one is down.
        /// </summary>
        [Column(TypeName = "decimal(9,6)")]
        public decimal? Latitude { get; set; } = null;

        [Column(TypeName = "decimal(9,6)")]
        public decimal? Longitude { get; set; } = null;

        /// <summary>The Limen shopper this customer is, per enterprise; cleared when the shopper deletes the account.</summary>
        [MaxLength(64)]
        public string? ExternalShopperId { get; set; } = null;

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; } = null;
        public virtual Enterprise? Enterprise { get; set; } = null;

        public virtual IEnumerable<Order>? Order { get; set; } = null;
    }
}
