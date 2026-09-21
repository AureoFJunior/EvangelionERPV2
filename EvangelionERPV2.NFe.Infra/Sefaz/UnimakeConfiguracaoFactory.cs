using System.Security.Cryptography.X509Certificates;

using EvangelionERPV2.Shared.Entities;

using Unimake.Business.DFe.Servicos;

namespace EvangelionERPV2.NFeModule.Infra.Sefaz
{
    /// <summary>
    /// Maps a tenant's fiscal profile onto the fiscal library's <see cref="Configuracao"/>.
    ///
    /// Pure: no I/O, no network, no certificate loading. Every failure it can raise happens before
    /// a single byte reaches SEFAZ and before a number is consumed, which is the cheapest place in
    /// the whole issuance path for something to go wrong.
    /// </summary>
    public static class UnimakeConfiguracaoFactory
    {
        /// <summary>
        /// v1 UF allowlist: SP, PR, SC, RS. Endpoint resolution was verified for exactly these four
        /// (docs/nfe/HANDOFF.md §7.3); anything else would be transmitted to an unverified
        /// autorizador.
        /// </summary>
        private static readonly short[] AllowedUf = [35, 41, 42, 43];

        /// <summary>
        /// Milliseconds. Set deliberately rather than left to the library default because it is the
        /// <b>only</b> bound on a transmission: the library is 100% synchronous (zero <c>*Async</c>
        /// methods in the assembly), so <c>Executar()</c> blocks and a <see cref="CancellationToken"/>
        /// cannot stop bytes already in flight. That is also why <c>TransmittedAtUtc</c> is committed
        /// before the call rather than after it.
        /// </summary>
        private const int WebServiceTimeoutMilliseconds = 60_000;

        /// <param name="csc">
        /// The tenant's Código de Segurança do Contribuinte, resolved from Secrets Manager by the
        /// caller. Required for NFCe and ignored for modelo 55.
        /// </param>
        public static Configuracao Create(
            EnterpriseFiscalSettings settings,
            NFeDocumentType type,
            X509Certificate2 certificate,
            string? csc = null,
            byte tipoEmissao = 1)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(certificate);

            if (Array.IndexOf(AllowedUf, settings.CodigoUfIbge) < 0)
            {
                throw new InvalidOperationException(
                    $"Enterprise fiscal settings carry cUF {settings.CodigoUfIbge}, which is outside the v1 allowlist (35 SP, 41 PR, 42 SC, 43 RS). " +
                    "Issuance for that UF has never been verified against its autorizador.");
            }

            if (settings.Environment is not (1 or 2))
            {
                throw new InvalidOperationException(
                    $"Enterprise fiscal settings carry tpAmb {settings.Environment}; it must be 1 (produção) or 2 (homologação).");
            }

            if (string.IsNullOrWhiteSpace(settings.SchemaVersion))
            {
                throw new InvalidOperationException("Enterprise fiscal settings carry no schema version.");
            }

            var isNFCe = type == NFeDocumentType.NFCe;

            if (tipoEmissao is not (1 or 9))
                throw new InvalidOperationException($"tpEmis {tipoEmissao} is not supported; only 1 (normal) and 9 (contingência off-line NFC-e) are.");

            if (tipoEmissao == 9 && !isNFCe)
                throw new InvalidOperationException("tpEmis 9 is contingência off-line NFC-e and cannot be used for modelo 55.");

            var configuracao = new Configuracao
            {
                TipoDFe = isNFCe ? TipoDFe.NFCe : TipoDFe.NFe,
                Modelo = isNFCe ? ModeloDFe.NFCe : ModeloDFe.NFe,

                CodigoUF = settings.CodigoUfIbge,
                TipoAmbiente = (TipoAmbiente)settings.Environment,

                TipoEmissao = tipoEmissao == 9 ? TipoEmissao.ContingenciaOffLine : TipoEmissao.Normal,

                SchemaVersao = settings.SchemaVersion,

                CertificadoDigital = certificate,

                CertificadoKeyStorageFlags = X509KeyStorageFlags.EphemeralKeySet,

                TimeOutWebServiceConnect = WebServiceTimeoutMilliseconds
            };

            if (isNFCe)
            {
                if (!settings.CscId.HasValue)
                    throw new InvalidOperationException("NFCe requires a CSC id on the enterprise fiscal settings; none is set.");

                if (string.IsNullOrWhiteSpace(csc))
                {
                    throw new InvalidOperationException(
                        "NFCe requires the CSC secret. It lives in Secrets Manager at evangelion/{env}/nfe/csc/{enterpriseId} and must be resolved by the caller.");
                }

                configuracao.CSCIDToken = settings.CscId.Value;
                configuracao.CSC = csc;
            }
            else if (!string.IsNullOrWhiteSpace(csc))
            {
                throw new InvalidOperationException("A CSC was supplied for modelo 55, which has no consumer QR code.");
            }

            // Deliberately NOT set: WebEnderecoProducao, WebEnderecoHomologacao, RequestURI* and
            // every other URL property. The library resolves the SEFAZ endpoint from CodigoUF plus
            // TipoAmbiente alone. A tenant-reachable SEFAZ URL is an SSRF hole, so these must never
            // gain a column, a DTO field or a config binding (docs/nfe/SPEC-engineering.md §7).
            return configuracao;
        }
    }
}
