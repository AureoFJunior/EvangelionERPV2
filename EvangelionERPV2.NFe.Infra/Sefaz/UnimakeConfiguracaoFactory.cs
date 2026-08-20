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

        public static Configuracao Create(
            EnterpriseFiscalSettings settings,
            NFeDocumentType type,
            X509Certificate2 certificate)
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

            var configuracao = new Configuracao
            {
                // Two different enums, both required, and cross-wiring them compiles silently:
                // TipoDFe.NFe is 0 and TipoDFe.NFCe is 1, while the 55/65 values live on ModeloDFe
                // (docs/nfe/HANDOFF.md §7.2). Keep both lines together so they are read together.
                TipoDFe = isNFCe ? TipoDFe.NFCe : TipoDFe.NFe,
                Modelo = isNFCe ? ModeloDFe.NFCe : ModeloDFe.NFe,

                CodigoUF = settings.CodigoUfIbge,
                TipoAmbiente = (TipoAmbiente)settings.Environment,

                // v1 has no contingency. EnterpriseFiscalSettings.ContingencyEnabled is an inert
                // placeholder that nothing reads (see the entity's own remark) — deliberately not
                // consulted here, so enabling contingency stays a code change rather than a config
                // change that silently alters tpEmis on live issuance.
                TipoEmissao = TipoEmissao.Normal,

                SchemaVersao = settings.SchemaVersion,

                CertificadoDigital = certificate,

                // EphemeralKeySet is what carried the phase-0 Linux signing gate: the private key
                // never touches a user key store, which is the single most common failure mode for
                // Brazilian fiscal libraries on Linux (docs/nfe/HANDOFF.md §7, check (b)).
                CertificadoKeyStorageFlags = X509KeyStorageFlags.EphemeralKeySet,

                TimeOutWebServiceConnect = WebServiceTimeoutMilliseconds
            };

            if (isNFCe && settings.CscId.HasValue)
            {
                // ponytail: idToken only. The CSC secret it pairs with lives in Secrets Manager at
                // evangelion/{env}/nfe/csc/{enterpriseId} and is wired in phase 6 along with the
                // QR code; until then an NFCe built from this configuration cannot be signed for
                // the consumer QR. Set Configuracao.CSC from the resolved secret there.
                configuracao.CSCIDToken = settings.CscId.Value;
            }

            // Deliberately NOT set: WebEnderecoProducao, WebEnderecoHomologacao, RequestURI* and
            // every other URL property. The library resolves the SEFAZ endpoint from CodigoUF plus
            // TipoAmbiente alone. A tenant-reachable SEFAZ URL is an SSRF hole, so these must never
            // gain a column, a DTO field or a config binding (docs/nfe/SPEC-engineering.md §7).
            return configuracao;
        }
    }
}
