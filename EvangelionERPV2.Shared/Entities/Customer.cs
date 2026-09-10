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
        /// <remarks>
        /// Exposure is computed, never stored: a stored running total drifts the moment a boleto is
        /// settled outside the app, and the receivable rule already lives in
        /// <see cref="Utils.ReceivableResolver"/>. Zero is a real value here and means cash only.
        /// </remarks>
        public decimal? CreditLimit { get; set; } = null;

        #region Fiscal address

        // enderDest on an NFe 4.00 needs xLgr, nro, xCpl, xBairro, cMun, xMun, UF and CEP as
        // separate fields; Adress above is one free-text blob and cannot populate any of them.
        // Structured here rather than on a separate profile entity because the destinatário needs
        // an address and an IE and nothing else — the emitente's EnterpriseFiscalSettings earns its
        // own table by also carrying CRT, certificate metadata and every tax default.
        //
        // All nullable and additive: a customer who never receives an NFe needs none of it, and
        // existing rows stay valid without a backfill. Completeness is enforced at issuance, where
        // the failure can name the missing field, not at the customer-registration boundary.

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
        /// <remarks>
        /// decimal(9,6) is roughly 11 cm of precision and is mandatory here: the global
        /// HavePrecision(18,2) convention would round -23.5505 to -23.55, which is about half a
        /// kilometre — enough to put a customer on the wrong side of a radius.
        /// </remarks>
        [Column(TypeName = "decimal(9,6)")]
        public decimal? Latitude { get; set; } = null;

        [Column(TypeName = "decimal(9,6)")]
        public decimal? Longitude { get; set; } = null;

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; } = null;
        public virtual Enterprise? Enterprise { get; set; } = null;

        public virtual IEnumerable<Order>? Order { get; set; } = null;
    }
}
