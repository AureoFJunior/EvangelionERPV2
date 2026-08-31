using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using EvangelionERPV2.NFeModule.Infra.Sefaz;
using EvangelionERPV2.Shared.Entities;

using Unimake.Business.DFe.Servicos;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    public class UnimakeConfiguracaoFactoryTests
    {
        private static EnterpriseFiscalSettings Settings(short cUf = 35, byte environment = 2) => new()
        {
            CodigoUfIbge = cUf,
            Uf = cUf switch { 35 => "SP", 41 => "PR", 42 => "SC", 43 => "RS", _ => "??" },
            Environment = environment,
            SchemaVersion = "4.00"
        };

        /// <summary>
        /// Self-signed and generated in-test. Nothing here reaches a socket, so no real A1
        /// certificate is needed — the same finding that unblocked the phase-0 gate.
        /// </summary>
        private static X509Certificate2 SelfSigned()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=EVANGELION TESTE LTDA:12345678000195", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            using var certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

            // Round-tripped through PKCS#12 so the instance carries an exportable private key,
            // which is the shape a real A1 certificate arrives in.
            return X509CertificateLoader.LoadPkcs12(
                certificate.Export(X509ContentType.Pkcs12, "test"), "test", X509KeyStorageFlags.EphemeralKeySet);
        }

        /// <summary>
        /// The whole point of this file. TipoDFe and ModeloDFe are two different enums carrying
        /// two different numbers for the same document, and swapping them compiles cleanly and
        /// fails silently at SEFAZ (docs/nfe/HANDOFF.md §7.2).
        /// </summary>
        [Fact]
        public void Create_ForNFe_SetsBothEnumsWithoutCrossWiringThem()
        {
            using var certificate = SelfSigned();

            var configuracao = UnimakeConfiguracaoFactory.Create(Settings(), NFeDocumentType.NFe, certificate);

            Assert.Equal(TipoDFe.NFe, configuracao.TipoDFe);
            Assert.Equal(0, (int)configuracao.TipoDFe);
            Assert.Equal(ModeloDFe.NFe, configuracao.Modelo);
            Assert.Equal(55, (int)configuracao.Modelo);
        }

        [Fact]
        public void Create_ForNFCe_SetsBothEnumsWithoutCrossWiringThem()
        {
            using var certificate = SelfSigned();
            var settings = Settings();
            settings.CscId = 7;

            var configuracao = UnimakeConfiguracaoFactory.Create(settings, NFeDocumentType.NFCe, certificate, "the-csc");

            Assert.Equal(TipoDFe.NFCe, configuracao.TipoDFe);
            Assert.Equal(1, (int)configuracao.TipoDFe);
            Assert.Equal(ModeloDFe.NFCe, configuracao.Modelo);
            Assert.Equal(65, (int)configuracao.Modelo);
        }

        [Theory]
        [InlineData((short)35)]
        [InlineData((short)41)]
        [InlineData((short)42)]
        [InlineData((short)43)]
        public void Create_MapsEveryAllowedUf(short cUf)
        {
            using var certificate = SelfSigned();

            var configuracao = UnimakeConfiguracaoFactory.Create(Settings(cUf), NFeDocumentType.NFe, certificate);

            Assert.Equal(cUf, configuracao.CodigoUF);
        }

        [Fact]
        public void Create_ForUfOutsideTheAllowlist_Throws()
        {
            using var certificate = SelfSigned();

            // MG (31): a perfectly real UF whose autorizador this system has never spoken to.
            var exception = Assert.Throws<InvalidOperationException>(
                () => UnimakeConfiguracaoFactory.Create(Settings(31), NFeDocumentType.NFe, certificate));

            Assert.Contains("31", exception.Message);
        }

        [Theory]
        [InlineData((byte)1, TipoAmbiente.Producao)]
        [InlineData((byte)2, TipoAmbiente.Homologacao)]
        public void Create_MapsEnvironmentToTipoAmbiente(byte environment, TipoAmbiente expected)
        {
            using var certificate = SelfSigned();

            var configuracao = UnimakeConfiguracaoFactory.Create(
                Settings(environment: environment), NFeDocumentType.NFe, certificate);

            Assert.Equal(expected, configuracao.TipoAmbiente);
        }

        [Fact]
        public void Create_ForUnknownEnvironment_Throws() =>
            Assert.Throws<InvalidOperationException>(() =>
            {
                using var certificate = SelfSigned();
                return UnimakeConfiguracaoFactory.Create(Settings(environment: 3), NFeDocumentType.NFe, certificate);
            });

        /// <summary>
        /// A tenant-reachable SEFAZ URL is an SSRF hole. The library resolves the endpoint from
        /// cUF and tpAmb by itself, so these properties must stay untouched forever.
        /// </summary>
        [Fact]
        public void Create_NeverPopulatesAnEndpointUrl()
        {
            using var certificate = SelfSigned();

            var configuracao = UnimakeConfiguracaoFactory.Create(Settings(), NFeDocumentType.NFe, certificate);

            Assert.True(string.IsNullOrEmpty(configuracao.WebEnderecoProducao));
            Assert.True(string.IsNullOrEmpty(configuracao.WebEnderecoHomologacao));
            Assert.True(string.IsNullOrEmpty(configuracao.RequestURIProducao));
            Assert.True(string.IsNullOrEmpty(configuracao.RequestURIHomologacao));
        }

        [Fact]
        public void Create_CarriesTheCertificateAndTheEphemeralKeyFlag()
        {
            using var certificate = SelfSigned();

            var configuracao = UnimakeConfiguracaoFactory.Create(Settings(), NFeDocumentType.NFe, certificate);

            Assert.Same(certificate, configuracao.CertificadoDigital);
            Assert.Equal(X509KeyStorageFlags.EphemeralKeySet, configuracao.CertificadoKeyStorageFlags);
            Assert.Equal("4.00", configuracao.SchemaVersao);
            Assert.Equal(TipoEmissao.Normal, configuracao.TipoEmissao);
            Assert.True(configuracao.TimeOutWebServiceConnect > 0);
        }

        [Fact]
        public void Create_ForNFCe_CarriesTheCscIdToken()
        {
            using var certificate = SelfSigned();
            var settings = Settings();
            settings.CscId = 7;

            var configuracao = UnimakeConfiguracaoFactory.Create(settings, NFeDocumentType.NFCe, certificate, "the-csc");

            Assert.Equal(7, configuracao.CSCIDToken);
            Assert.Equal("the-csc", configuracao.CSC);
        }

        /// <summary>
        /// The consumer QR is an HMAC over the access key keyed by the CSC. Without one the note
        /// carries a QR nobody can verify, so this is refused before a number is consumed rather
        /// than discovered by a customer holding a phone.
        /// </summary>
        [Fact]
        public void Create_ForNFCe_WithNoCsc_IsRefused()
        {
            using var certificate = SelfSigned();
            var settings = Settings();
            settings.CscId = 7;

            var withoutSecret = Assert.Throws<InvalidOperationException>(
                () => UnimakeConfiguracaoFactory.Create(settings, NFeDocumentType.NFCe, certificate));
            Assert.Contains("CSC secret", withoutSecret.Message);

            var blank = Assert.Throws<InvalidOperationException>(
                () => UnimakeConfiguracaoFactory.Create(settings, NFeDocumentType.NFCe, certificate, "   "));
            Assert.Contains("CSC secret", blank.Message);
        }

        [Fact]
        public void Create_ForNFCe_WithNoCscId_IsRefused()
        {
            using var certificate = SelfSigned();
            var settings = Settings();
            settings.CscId = null;

            var exception = Assert.Throws<InvalidOperationException>(
                () => UnimakeConfiguracaoFactory.Create(settings, NFeDocumentType.NFCe, certificate, "the-csc"));

            Assert.Contains("CSC id", exception.Message);
        }

        /// <summary>
        /// Modelo 55 has no consumer QR. A caller passing a CSC believes it is issuing an NFCe and
        /// is not — refused rather than silently dropping a signing key on the floor.
        /// </summary>
        [Fact]
        public void Create_ForNFe_WithACsc_IsRefused()
        {
            using var certificate = SelfSigned();

            Assert.Throws<InvalidOperationException>(
                () => UnimakeConfiguracaoFactory.Create(Settings(), NFeDocumentType.NFe, certificate, "the-csc"));
        }

        [Fact]
        public void Create_ForNFe_IgnoresTheCscIdToken()
        {
            using var certificate = SelfSigned();
            var settings = Settings();
            settings.CscId = 7;

            var configuracao = UnimakeConfiguracaoFactory.Create(settings, NFeDocumentType.NFe, certificate);

            Assert.Equal(0, configuracao.CSCIDToken);
        }
    }
}
