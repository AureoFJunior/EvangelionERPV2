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

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; } = null;
        public virtual Enterprise? Enterprise { get; set; } = null;

        public virtual IEnumerable<Order>? Order { get; set; } = null;
    }
}
