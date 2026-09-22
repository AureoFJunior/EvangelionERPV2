using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// Per-tenant fiscal profile driving NFe (model 55) and NFCe (model 65) issuance.
    /// Admin-only surface: a wrong CNPJ, CRT or default CFOP silently mis-taxes every
    /// future document, and the only remedy is cancellation inside the legal window.
    /// </summary>
    // The EnterpriseId unique filtered index is declared fluently in AppDbContext —
    // [Index] cannot express HasFilter.
    // Certificate material (.pfx bytes and password) and the NFCe CSC token are NEVER
    // stored here. They live in AWS Secrets Manager; this table keeps metadata only.
    [Table("EnterpriseFiscalSettings")]
    public class EnterpriseFiscalSettings : BaseEntity
    {
        #region Emitente identity

        [MaxLength(14)]
        public string Cnpj { get; set; } = string.Empty;

        [MaxLength(14)]
        public string InscricaoEstadual { get; set; } = string.Empty;

        /// <summary>Only relevant for ISS-liable operations; not required to issue NFe/NFCe.</summary>
        [MaxLength(15)]
        public string? InscricaoMunicipal { get; set; }

        /// <summary>xEmit. Distinct from Enterprise.Name — the fiscal name has its own rules.</summary>
        [MaxLength(60)]
        public string RazaoSocial { get; set; } = string.Empty;

        [MaxLength(60)]
        public string? NomeFantasia { get; set; }

        /// <summary>
        /// Código de Regime Tributário. 1 = Simples Nacional, 2 = Simples excess sublimit,
        /// 3 = Regime Normal, 4 = MEI. Decides whether items carry CSOSN (1/2) or CST (3).
        /// </summary>
        public byte Crt { get; set; } = 3;

        #endregion

        #region Emitente address

        [MaxLength(60)]
        public string Logradouro { get; set; } = string.Empty;

        [MaxLength(60)]
        public string Numero { get; set; } = string.Empty;

        [MaxLength(60)]
        public string? Complemento { get; set; }

        [MaxLength(60)]
        public string Bairro { get; set; } = string.Empty;

        /// <summary>cMun — 7-digit IBGE municipality code.</summary>
        [MaxLength(7)]
        public string CodigoMunicipioIbge { get; set; } = string.Empty;

        [MaxLength(60)]
        public string Municipio { get; set; } = string.Empty;

        /// <summary>v1 allowlist: SP, RS, SC, PR.</summary>
        [MaxLength(2)]
        public string Uf { get; set; } = string.Empty;

        /// <summary>cUF — SP=35, PR=41, SC=42, RS=43. Derived from Uf, persisted for the SEFAZ client.</summary>
        public short CodigoUfIbge { get; set; }

        [MaxLength(8)]
        public string Cep { get; set; } = string.Empty;

        [MaxLength(4)]
        public string CodigoPais { get; set; } = "1058";

        [MaxLength(60)]
        public string Pais { get; set; } = "BRASIL";

        [MaxLength(14)]
        public string? Telefone { get; set; }

        #endregion

        #region Environment and operation

        /// <summary>tpAmb. 1 = Produção, 2 = Homologação. Per tenant, so one enterprise can go
        /// live while the rest stay in homologação.</summary>
        public byte Environment { get; set; } = 2;

        /// <summary>Per-tenant issuance switch, AND-ed with the global kill switch in NFeSettings.</summary>
        public bool Enabled { get; set; }

        public bool NFCeEnabled { get; set; }

        [MaxLength(3)]
        public string NFeSeries { get; set; } = "1";

        [MaxLength(3)]
        public string NFCeSeries { get; set; } = "1";

        /// <summary>
        /// The series offline NFC-e is issued under. Distinct from <see cref="NFCeSeries"/> on
        /// purpose.
        /// </summary>
        [MaxLength(3)]
        public string NFCeContingencySeries { get; set; } = "9";

        /// <summary>
        /// idToken of the NFCe CSC. Not a secret on its own — the CSC token it pairs with is,
        /// and lives in Secrets Manager. Null until the tenant configures NFCe.
        /// </summary>
        public int? CscId { get; set; }

        [MaxLength(5)]
        public string SchemaVersion { get; set; } = "4.00";

        #endregion

        #region Legal deadlines

        /// <summary>NFe model 55 — 24 h (Ajuste SINIEF 07/2005).</summary>
        public int NFeCancellationWindowMinutes { get; set; } = 1440;

        /// <summary>NFCe model 65 — 30 min national reference. UNVERIFIED per UF.</summary>
        public int NFCeCancellationWindowMinutes { get; set; } = 30;

        /// <summary>
        /// Above this total, identifying the consumer on an NFCe is mandatory.
        /// R$ 10.000 reference value, UNVERIFIED per UF.
        /// </summary>
        public decimal NFCeCustomerIdentificationThreshold { get; set; } = 10000m;

        #endregion

        #region Certificate metadata

        [MaxLength(64)]
        public string? CertThumbprint { get; set; }

        [MaxLength(256)]
        public string? CertSubjectCn { get; set; }

        public DateTime? CertNotAfterUtc { get; set; }

        public DateTime? CertUploadedAtUtc { get; set; }

        #endregion

        #region Fiscal defaults

        /// <summary>
        /// natOp — the free-text nature of the operation, printed on the DANFE.
        /// </summary>
        [MaxLength(60)]
        public string DefaultNatOp { get; set; } = "VENDA DE MERCADORIA";

        [MaxLength(4)]
        public string DefaultCfopIntra { get; set; } = "5102";

        [MaxLength(4)]
        public string DefaultCfopInter { get; set; } = "6102";

        /// <summary>Used when Crt = 3 (Regime Normal).</summary>
        [MaxLength(3)]
        public string? DefaultCst { get; set; }

        /// <summary>Used when Crt = 1 or 2 (Simples Nacional).</summary>
        [MaxLength(3)]
        public string? DefaultCsosn { get; set; }

        public byte DefaultOrigem { get; set; }

        [Column(TypeName = "decimal(5,4)")]
        public decimal DefaultAliquotaIcms { get; set; }

        [MaxLength(2)]
        public string DefaultCstPis { get; set; } = "07";

        [Column(TypeName = "decimal(5,4)")]
        public decimal DefaultAliquotaPis { get; set; }

        [MaxLength(2)]
        public string DefaultCstCofins { get; set; } = "07";

        [Column(TypeName = "decimal(5,4)")]
        public decimal DefaultAliquotaCofins { get; set; }

        [MaxLength(6)]
        public string DefaultUnidadeTributavel { get; set; } = "UN";

        /// <summary>
        /// pCredSN — the ICMS credit a Simples Nacional emitter passes to its buyer, as a fraction
        /// of the item value (0.0254 is 2,54%).
        /// </summary>
        [Column(TypeName = "decimal(5,4)")]
        public decimal? AliquotaCreditoSn { get; set; }

        #endregion

        #region Contingency

        // ponytail: inert placeholders. Offline NFCe and SVC contingency are out of scope v1;
        // these exist so enabling them later is a code change, not a schema change. Nothing reads
        // them today.

        public bool ContingencyEnabled { get; set; }

        public DateTime? ContingencyEnteredAt { get; set; }

        [MaxLength(256)]
        public string? ContingencyReason { get; set; }

        #endregion

        [ForeignKey(nameof(Enterprise))]
        public Guid EnterpriseId { get; set; }
        public virtual Enterprise? Enterprise { get; set; }
    }
}
