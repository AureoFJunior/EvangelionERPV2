using EvangelionERPV2.NFeModule.Infra.Certificates;
using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.NFeModule.Infra.Sefaz;
using EvangelionERPV2.NFeModule.Infra.Xml;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;

using System.Reflection;
using Microsoft.Extensions.Options;
using Moq;

using System.Security.Cryptography.X509Certificates;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    /// <summary>
    /// Covers every path that ends before a socket is opened — the paths where the caller's
    /// allocated number must survive.
    /// </summary>
    /// <remarks>
    /// Nothing here transmits. <c>NeverTransmitted</c> is the assertion that matters: it is what
    /// tells <c>NFeService</c> the same nNF may be sent again, and getting it wrong in either
    /// direction is expensive. False when nothing was sent burns a number that only inutilização
    /// can account for; true when something might have been sent double-emits a real note.
    /// </remarks>
    public class SefazNFeProviderTests
    {
        private static readonly Guid EnterpriseId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        private const string EmitterCnpj = "12345678000195";

        private static EnterpriseFiscalSettings Settings() => new()
        {
            EnterpriseId = EnterpriseId,
            Cnpj = EmitterCnpj,
            InscricaoEstadual = "123456789",
            RazaoSocial = "EVANGELION COMERCIO LTDA",
            Crt = 3,
            Logradouro = "RUA DAS FLORES",
            Numero = "100",
            Bairro = "CENTRO",
            CodigoMunicipioIbge = "4314902",
            Municipio = "PORTO ALEGRE",
            Uf = "RS",
            CodigoUfIbge = 43,
            Cep = "90010000",
            Environment = 2,
            SchemaVersion = "4.00",
            DefaultCfopIntra = "5102",
            DefaultCfopInter = "6102",
            DefaultCst = "00",
            DefaultCstPis = "07",
            DefaultCstCofins = "07",
            DefaultUnidadeTributavel = "UN"
        };

        private static NFeIssueContext Context(Customer? customer = null)
        {
            var orderId = Guid.NewGuid();
            var product = new Product { Id = Guid.NewGuid(), Name = "WIDGET", Ncm = "73269090" };

            return new NFeIssueContext
            {
                Order = new Order { Id = orderId, EnterpriseId = EnterpriseId, TotalValue = 10m },
                Items =
                [
                    new OrderedProduct
                    {
                        Id = Guid.NewGuid(), OrderId = orderId, ProductId = product.Id,
                        Product = product, Quantity = 1m, Value = 10m, UnitOfMeasure = "UN"
                    }
                ],
                FiscalSettings = Settings(),
                Customer = customer ?? new Customer
                {
                    Id = Guid.NewGuid(),
                    Name = "CLIENTE",
                    Document = "98765432000198",
                    Logradouro = "AVENIDA BRASIL",
                    Numero = "2000",
                    Bairro = "SAO JOAO",
                    CodigoMunicipioIbge = "4314902",
                    Municipio = "PORTO ALEGRE",
                    Uf = "RS",
                    Cep = "90230000"
                },
                Type = NFeDocumentType.NFe,
                AccessKey = NFeAccessKeyBuilder.Build(43, DateTime.UtcNow.AddHours(-3), EmitterCnpj, 55, 1, 7, 1, "00099887"),
                Number = "7",
                Series = "1"
            };
        }

        #region infRespTec resolution

        [Fact]
        public async Task ResolveResponsibleTechnician_WithNothingConfigured_ReturnsNull()
        {
            Assert.Null(await ResolveRespTec(new NFeRespTecSettings()));
        }

        [Fact]
        public async Task ResolveResponsibleTechnician_WithEveryValue_ReturnsTheGroupWithDigitsOnly()
        {
            var technician = await ResolveRespTec(new NFeRespTecSettings
            {
                Cnpj = "12.345.678/0001-99",
                ContactName = " Aureo ",
                Email = "dev@nerv.jp",
                Phone = "(51) 99999-0000"
            });

            Assert.NotNull(technician);
            Assert.Equal("12345678000199", technician!.Cnpj);
            Assert.Equal("Aureo", technician.ContactName);
            Assert.Equal("51999990000", technician.Phone);

            // Without a resolver there is no CSRT, and a note is valid without one.
            Assert.Null(technician.IdCsrt);
            Assert.Null(technician.Csrt);
        }

        [Theory]
        [InlineData("12345678000199", "Aureo", "dev@nerv.jp", "")]
        [InlineData("12345678000199", "Aureo", "", "5199990000")]
        [InlineData("123", "Aureo", "dev@nerv.jp", "5199990000")]
        public async Task ResolveResponsibleTechnician_WhenHalfConfigured_OmitsTheGroup(
            string cnpj, string contact, string email, string phone)
        {
            // A partial group is a rejection at SEFAZ whose motivo points at the field rather than
            // at the configuration. Omitting it is what every note did before these settings.
            var technician = await ResolveRespTec(new NFeRespTecSettings
            {
                Cnpj = cnpj,
                ContactName = contact,
                Email = email,
                Phone = phone
            });

            Assert.Null(technician);
        }

        private static async Task<NFeResponsibleTechnician?> ResolveRespTec(NFeRespTecSettings settings)
        {
            var provider = new SefazNFeProvider(Certificates().Object, Options.Create(settings));
            var method = typeof(SefazNFeProvider).GetMethod(
                "ResolveResponsibleTechnicianAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            return await (Task<NFeResponsibleTechnician?>)method.Invoke(provider, [CancellationToken.None])!;
        }

        #endregion

        private static Mock<IEnterpriseCertificateProvider> Certificates() => new(MockBehavior.Strict);

        [Fact]
        public async Task IssueAsync_WhenTheCertificateCannotBeLoaded_ReportsNeverTransmitted()
        {
            var certificates = Certificates();
            certificates
                .Setup(x => x.GetAsync(EnterpriseId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NFeCertificateException("No certificate is stored for this enterprise."));

            var result = await new SefazNFeProvider(certificates.Object).IssueAsync(Context());

            Assert.Equal(NFeStatus.Error, result.Status);
            Assert.True(result.NeverTransmitted);
            Assert.False(result.IsTransient);
            Assert.Contains("No certificate", result.XMotivo);
        }

        [Fact]
        public async Task IssueAsync_WhenTheDocumentCannotBeBuilt_ReportsNeverTransmitted()
        {
            var incomplete = new Customer { Id = Guid.NewGuid(), Name = "CLIENTE", Document = "98765432000198" };

            var certificates = Certificates();
            certificates
                .Setup(x => x.GetAsync(EnterpriseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(SelfSigned());

            var result = await new SefazNFeProvider(certificates.Object).IssueAsync(Context(incomplete));

            // The customer has no fiscal address, so NFeXmlBuilder refuses. Nothing reached the wire
            // and the caller keeps its number.
            Assert.Equal(NFeStatus.Error, result.Status);
            Assert.True(result.NeverTransmitted);
        }

        [Theory]
        [InlineData("")]
        [InlineData("curta demais")]
        public async Task CancelAsync_RejectsAJustificationTheEventSchemaWouldRefuse(string reason)
        {
            var certificates = Certificates();

            var result = await new SefazNFeProvider(certificates.Object)
                .CancelAsync("43250912345678000195550010000000071100998876", "135250000000001", reason, Settings());

            Assert.True(result.NeverTransmitted);
            Assert.NotEqual(NFeStatus.Cancelled, result.Status);

            // Checked before anything else: a refused event still costs a round trip on a note whose
            // cancellation window is running out.
            certificates.Verify(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CancelAsync_RefusesADocumentWithNoAuthorizationProtocol()
        {
            var certificates = Certificates();

            var result = await new SefazNFeProvider(certificates.Object).CancelAsync(
                "43250912345678000195550010000000071100998876",
                protocol: "",
                reason: "Pedido cancelado pelo cliente antes da entrega.",
                fiscalSettings: Settings());

            Assert.True(result.NeverTransmitted);
            Assert.NotEqual(NFeStatus.Cancelled, result.Status);
            certificates.Verify(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ConsultAsync_WhenTheCertificateCannotBeLoaded_DoesNotClaimTheNoteIsMissing()
        {
            var certificates = Certificates();
            certificates
                .Setup(x => x.GetAsync(EnterpriseId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NFeCertificateException("The stored certificate has expired."));

            var result = await new SefazNFeProvider(certificates.Object)
                .ConsultAsync("43250912345678000195550010000000071100998876", Settings());

            // NeverTransmitted here is about the consult, not about the note: we failed to ask, so
            // the note's outcome is exactly as unknown as it was before.
            Assert.Equal(NFeStatus.Error, result.Status);
            Assert.NotEqual(NFeStatus.Authorized, result.Status);
        }

        /// <summary>A throwaway A1-shaped certificate. Never reaches a socket in these tests.</summary>
        private static X509Certificate2 SelfSigned()
        {
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var request = new CertificateRequest("CN=EVANGELION TESTE:12345678000195", rsa,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);

            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return X509CertificateLoader.LoadPkcs12(
                certificate.Export(X509ContentType.Pkcs12, "senha"), "senha", X509KeyStorageFlags.EphemeralKeySet);
        }
    }
}
