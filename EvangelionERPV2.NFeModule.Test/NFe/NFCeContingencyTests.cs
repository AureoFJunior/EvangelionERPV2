using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.NFeModule.Infra.Sefaz;
using EvangelionERPV2.NFeModule.Infra.Xml;
using EvangelionERPV2.Shared.Entities;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Unimake.Business.DFe.Servicos;
using Unimake.Business.DFe.Xml.NFe;
using Xunit;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    /// <summary>
    /// Contingência off-line NFC-e (tpEmis 9): the shop keeps selling with SEFAZ unreachable, the
    /// customer walks out with a printed note, and the note is transmitted afterwards.
    /// </summary>
    public class NFCeContingencyTests
    {
        private static readonly DateTime EnteredAt = new(2026, 8, 31, 14, 30, 0, DateTimeKind.Utc);
        private const string Justification = "SEFAZ inacessivel no momento da venda";

        [Fact]
        public void AnOfflineNote_CarriesTpEmis9WithItsDhContAndXJust()
        {
            var ide = IdeOf(Offline());

            Assert.Equal(TipoEmissao.ContingenciaOffLine, ide.TpEmis);
            Assert.Equal(9, (int)ide.TpEmis);
            Assert.Equal(Justification, ide.XJust);

            Assert.Equal(TimeSpan.FromHours(-3), ide.DhCont.Offset);
            Assert.Equal(EnteredAt, ide.DhCont.UtcDateTime);
        }

        [Fact]
        public void AnOrdinaryNote_CarriesNeitherDhContNorXJust()
        {
            var ide = IdeOf(NFCeContext());

            Assert.Equal(TipoEmissao.Normal, ide.TpEmis);
            Assert.Null(ide.XJust);
            Assert.Equal(default, ide.DhCont);
        }

        [Theory]
        [InlineData("")]
        [InlineData("too short")]
        public void AnOfflineNote_WithoutAUsableJustification_IsRefusedBeforeItIsPrinted(string justification)
        {
            Assert.Throws<InvalidOperationException>(() => NFeXmlBuilder.Build(Offline(justification), null));
        }

        [Fact]
        public void AnOfflineNote_WithNoDhCont_IsRefused()
        {
            Assert.Throws<InvalidOperationException>(() => NFeXmlBuilder.Build(Offline(withDate: false), null));
        }

        /// <summary>
        /// tpEmis 9 is the NFC-e offline mode by definition. Modelo 55 has its own contingency
        /// modes and none of them is this one; letting it through builds a note no autorizador
        /// accepts.
        /// </summary>
        [Fact]
        public void Modelo55_CannotBorrowTheNFCeOfflineMode()
        {
            using var certificate = SelfSigned();

            Assert.Throws<InvalidOperationException>(() => UnimakeConfiguracaoFactory.Create(
                NFeXmlBuilderTestSettings(), NFeDocumentType.NFe, certificate, csc: null, tipoEmissao: 9));
        }

        [Fact]
        public void TheConfiguration_AgreesWithTheDocumentAboutTpEmis()
        {
            using var certificate = SelfSigned();
            var settings = NFeXmlBuilderTestSettings();
            settings.CscId = 7;

            var offline = UnimakeConfiguracaoFactory.Create(
                settings, NFeDocumentType.NFCe, certificate, "the-csc", tipoEmissao: 9);
            Assert.Equal(TipoEmissao.ContingenciaOffLine, offline.TipoEmissao);

            var normal = UnimakeConfiguracaoFactory.Create(
                settings, NFeDocumentType.NFCe, certificate, "the-csc");
            Assert.Equal(TipoEmissao.Normal, normal.TipoEmissao);
        }

        /// <summary>
        /// The ordinary counter sale: nobody gives a CPF for a bottle of water, and the dest group
        /// is omitted rather than faked.
        /// </summary>
        [Fact]
        public void AnAnonymousCounterSale_OmitsTheDestinatarioEntirely()
        {
            var note = NFeXmlBuilder.Build(NFCeContext(), null).NFe[0].InfNFeField;

            Assert.Null(note.Dest);
        }

        [Fact]
        public void AtOrAboveTheThreshold_TheConsumerMustBeIdentified()
        {
            var context = WithTotal(10_000m);

            var exception = Assert.Throws<InvalidOperationException>(() => NFeXmlBuilder.Build(context, null));
            Assert.Contains("CPF", exception.Message);

            Assert.Null(NFeXmlBuilder.Build(WithTotal(9_999.99m), null).NFe[0].InfNFeField.Dest);
        }

        [Fact]
        public void Modelo55_StillDemandsADestinatario()
        {
            var source = NFCeContext();
            var context = new NFeIssueContext
            {
                Order = source.Order,
                Items = source.Items,
                FiscalSettings = source.FiscalSettings,
                Customer = null,
                Type = NFeDocumentType.NFe,
                AccessKey = source.AccessKey,
                Number = source.Number,
                Series = source.Series
            };

            Assert.Throws<InvalidOperationException>(() => NFeXmlBuilder.Build(context, null));
        }

        private static NFeIssueContext WithTotal(decimal total)
        {
            var source = NFCeContext();
            source.Order.TotalValue = total;
            return source;
        }

        private static NFeIssueContext NFCeContext(byte tipoEmissao = 1) => NFeXmlBuilderTestContext(tipoEmissao);

        [Fact]
        public void AnUnsupportedTpEmis_IsRefusedRatherThanTreatedAsNormal()
        {
            using var certificate = SelfSigned();

            Assert.Throws<InvalidOperationException>(() => UnimakeConfiguracaoFactory.Create(
                NFeXmlBuilderTestSettings(), NFeDocumentType.NFe, certificate, csc: null, tipoEmissao: 4));
        }

        private static Ide IdeOf(NFeIssueContext context) =>
            NFeXmlBuilder.Build(context, null).NFe[0].InfNFeField.Ide;

        /// <summary>An offline note. The chave is keyed with tpEmis 9 so it agrees with the document.</summary>
        private static NFeIssueContext Offline(string justification = Justification, DateTime? enteredAt = null, bool withDate = true)
        {
            var source = NFCeContext(tipoEmissao: 9);

            return new NFeIssueContext
            {
                Order = source.Order,
                Items = source.Items,
                FiscalSettings = source.FiscalSettings,
                Customer = null,
                Type = NFeDocumentType.NFCe,
                AccessKey = source.AccessKey,
                Number = source.Number,
                Series = source.Series,
                TipoEmissao = 9,
                ContingencyEnteredAt = withDate ? enteredAt ?? EnteredAt : null,
                ContingencyJustification = justification
            };
        }

        private static X509Certificate2 SelfSigned()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=Evangelion Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        }

        private static EnterpriseFiscalSettings NFeXmlBuilderTestSettings() => new()
        {
            EnterpriseId = Guid.NewGuid(),
            Cnpj = "12345678000195",
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
            DefaultCsosn = "102",
            DefaultOrigem = 0,
            DefaultAliquotaIcms = 0.18m,
            DefaultCstPis = "07",
            DefaultAliquotaPis = 0m,
            DefaultCstCofins = "07",
            DefaultAliquotaCofins = 0m,
            DefaultUnidadeTributavel = "UN"
        };

        private static NFeIssueContext NFeXmlBuilderTestContext(byte tipoEmissao = 1)
        {
            var settings = NFeXmlBuilderTestSettings();
            var orderId = Guid.NewGuid();
            var order = new Order { Id = orderId, EnterpriseId = settings.EnterpriseId, TotalValue = 250m };

            return new NFeIssueContext
            {
                Order = order,
                Items =
                [
                    new OrderedProduct
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderId,
                        ProductId = Guid.NewGuid(),
                        Product = new Product { Name = "WIDGET", Ncm = "84713012", UnitOfMeasure = "UN" },
                        Quantity = 2m,
                        Value = 125m,
                        UnitOfMeasure = "UN"
                    }
                ],
                FiscalSettings = settings,
                Customer = null,
                Type = NFeDocumentType.NFCe,
                AccessKey = AccessKeyForNow(settings, tipoEmissao),
                Number = "42",
                Series = "1",
                TipoEmissao = tipoEmissao,
                ContingencyEnteredAt = tipoEmissao == 9 ? EnteredAt : null,
                ContingencyJustification = tipoEmissao == 9 ? Justification : null
            };
        }

        private static string AccessKeyForNow(EnterpriseFiscalSettings settings, byte tipoEmissao) =>
            NFeAccessKeyBuilder.Build(
                settings.CodigoUfIbge,
                DateTime.UtcNow.AddHours(-3),
                settings.Cnpj,
                65,
                1,
                42,
                tipoEmissao,
                "12345678");
    }
}
