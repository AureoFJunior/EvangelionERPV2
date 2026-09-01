using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Admin-facing fiscal profile payload.
    /// </summary>
    /// <remarks>
    /// Deliberately carries NO secret material: no certificate bytes, no certificate password,
    /// no NFCe CSC token. Those live only in AWS Secrets Manager. The certificate fields below
    /// are read-only metadata, written by the certificate upload flow rather than by this DTO.
    /// EnterpriseFiscalSettingsDtoPrivacyTests fails the build if a secret-looking property
    /// is ever added here.
    /// </remarks>
    public class EnterpriseFiscalSettingsDTO
    {
        #region Emitente identity

        public string Cnpj { get; set; } = string.Empty;
        public string InscricaoEstadual { get; set; } = string.Empty;
        public string? InscricaoMunicipal { get; set; }
        public string RazaoSocial { get; set; } = string.Empty;
        public string? NomeFantasia { get; set; }
        public byte Crt { get; set; } = 3;

        #endregion

        #region Emitente address

        public string Logradouro { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string? Complemento { get; set; }
        public string Bairro { get; set; } = string.Empty;
        public string CodigoMunicipioIbge { get; set; } = string.Empty;
        public string Municipio { get; set; } = string.Empty;
        public string Uf { get; set; } = string.Empty;
        public string Cep { get; set; } = string.Empty;
        public string? Telefone { get; set; }

        // CodigoUfIbge is derived from Uf server-side and is not accepted from the client.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public short CodigoUfIbge { get; set; }

        #endregion

        #region Environment and operation

        public byte Environment { get; set; } = 2;
        public bool Enabled { get; set; }
        public bool NFCeEnabled { get; set; }
        public string NFeSeries { get; set; } = "1";
        public string NFCeSeries { get; set; } = "1";

        /// <summary>The paired CSC token is write-only and never travels in this payload.</summary>
        public int? CscId { get; set; }

        #endregion

        #region Legal deadlines

        public int NFeCancellationWindowMinutes { get; set; } = 1440;
        public int NFCeCancellationWindowMinutes { get; set; } = 30;
        public decimal NFCeCustomerIdentificationThreshold { get; set; } = 10000m;

        #endregion

        #region Certificate metadata (read-only)

        public string? CertThumbprint { get; set; }
        public string? CertSubjectCn { get; set; }
        public DateTime? CertNotAfterUtc { get; set; }
        public DateTime? CertUploadedAtUtc { get; set; }

        #endregion

        #region Fiscal defaults

        /// <summary>natOp, printed on the DANFE. One per tenant — see the entity.</summary>
        public string DefaultNatOp { get; set; } = "VENDA DE MERCADORIA";

        public string DefaultCfopIntra { get; set; } = "5102";
        public string DefaultCfopInter { get; set; } = "6102";
        public string? DefaultCst { get; set; }
        public string? DefaultCsosn { get; set; }
        public byte DefaultOrigem { get; set; }
        public decimal DefaultAliquotaIcms { get; set; }
        public string DefaultCstPis { get; set; } = "07";
        public decimal DefaultAliquotaPis { get; set; }
        public string DefaultCstCofins { get; set; } = "07";
        public decimal DefaultAliquotaCofins { get; set; }
        public string DefaultUnidadeTributavel { get; set; } = "UN";

        #endregion
    }
}
