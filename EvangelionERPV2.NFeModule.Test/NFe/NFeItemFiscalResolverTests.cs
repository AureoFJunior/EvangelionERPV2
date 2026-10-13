using EvangelionERPV2.NFeModule.Infra.Xml;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    public class NFeItemFiscalResolverTests
    {
        private static Product Product(string? ncm = "12345678") => new()
        {
            Id = Guid.NewGuid(),
            Name = "Widget",
            Ncm = ncm
        };

        private static EnterpriseFiscalSettings Settings(byte crt = 3) => new()
        {
            Crt = crt,
            DefaultCfopIntra = "5102",
            DefaultCfopInter = "6102",
            DefaultCst = "00",
            DefaultCsosn = "102",
            DefaultOrigem = 0,
            DefaultAliquotaIcms = 0.18m,
            DefaultCstPis = "07",
            DefaultAliquotaPis = 0.0165m,
            DefaultCstCofins = "07",
            DefaultAliquotaCofins = 0.076m,
            DefaultUnidadeTributavel = "UN"
        };

        [Fact]
        public void Resolve_ProductValues_OverrideEnterpriseDefaults()
        {
            var product = Product();
            product.CfopIntra = "5405";
            product.Origem = 2;
            product.UnidadeTributavel = "KG";
            product.Cst = "60";
            product.AliquotaIcms = 0.07m;
            product.CstPis = "01";
            product.AliquotaPis = 0.0765m;
            product.CstCofins = "01";
            product.AliquotaCofins = 0.0365m;
            product.Cest = "1234567";
            product.Cean = "07891234567895";
            product.CodigoBeneficioFiscal = "SP800001";

            var resolved = NFeItemFiscalResolver.Resolve(product, Settings(), isInterstate: false);

            Assert.Equal("5405", resolved.Cfop);
            Assert.Equal((byte)2, resolved.Origem);
            Assert.Equal("KG", resolved.UnidadeTributavel);
            Assert.Equal("60", resolved.Cst);
            Assert.Equal(0.07m, resolved.AliquotaIcms);
            Assert.Equal("01", resolved.CstPis);
            Assert.Equal(0.0765m, resolved.AliquotaPis);
            Assert.Equal("01", resolved.CstCofins);
            Assert.Equal(0.0365m, resolved.AliquotaCofins);
            Assert.Equal("1234567", resolved.Cest);
            Assert.Equal("07891234567895", resolved.Cean);
            Assert.Equal("SP800001", resolved.CodigoBeneficioFiscal);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Resolve_NullOrBlankProductValues_FallBackToEnterpriseDefaults(string? blank)
        {
            var product = Product();
            product.CfopIntra = blank;
            product.UnidadeTributavel = blank;
            product.Cst = blank;
            product.CstPis = blank;
            product.CstCofins = blank;

            var resolved = NFeItemFiscalResolver.Resolve(product, Settings(), isInterstate: false);

            Assert.Equal("5102", resolved.Cfop);
            Assert.Equal("UN", resolved.UnidadeTributavel);
            Assert.Equal("00", resolved.Cst);
            Assert.Equal("07", resolved.CstPis);
            Assert.Equal("07", resolved.CstCofins);
            Assert.Equal((byte)0, resolved.Origem);
            Assert.Equal(0.18m, resolved.AliquotaIcms);
            Assert.Equal(0.0165m, resolved.AliquotaPis);
            Assert.Equal(0.076m, resolved.AliquotaCofins);
        }

        [Fact]
        public void Resolve_NullCean_BecomesSemGtin()
        {
            var resolved = NFeItemFiscalResolver.Resolve(Product(), Settings(), isInterstate: false);

            Assert.Equal("SEM GTIN", resolved.Cean);
            Assert.Null(resolved.Cest);
            Assert.Null(resolved.CodigoBeneficioFiscal);
        }

        [Fact]
        public void Resolve_Interstate_PicksTheInterCfopPair()
        {
            var product = Product();
            product.CfopIntra = "5405";
            product.CfopInter = "6405";

            Assert.Equal("6405", NFeItemFiscalResolver.Resolve(product, Settings(), isInterstate: true).Cfop);
            Assert.Equal("5405", NFeItemFiscalResolver.Resolve(product, Settings(), isInterstate: false).Cfop);
        }

        [Fact]
        public void Resolve_Interstate_FallsBackToDefaultCfopInter()
        {
            var resolved = NFeItemFiscalResolver.Resolve(Product(), Settings(), isInterstate: true);

            Assert.Equal("6102", resolved.Cfop);
        }

        [Fact]
        public void Resolve_Crt3_SetsCstAndLeavesCsosnNull()
        {
            var resolved = NFeItemFiscalResolver.Resolve(Product(), Settings(crt: 3), isInterstate: false);

            Assert.Equal("00", resolved.Cst);
            Assert.Null(resolved.Csosn);
        }

        [Theory]
        [InlineData((byte)1)]
        [InlineData((byte)2)]
        [InlineData((byte)4)]
        public void Resolve_SimplesNacionalCrt_SetsCsosnAndLeavesCstNull(byte crt)
        {
            var product = Product();
            product.Cst = "00";

            var resolved = NFeItemFiscalResolver.Resolve(product, Settings(crt), isInterstate: false);

            Assert.Equal("102", resolved.Csosn);
            Assert.Null(resolved.Cst);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void Resolve_MissingNcm_Throws(string? ncm)
        {
            var product = Product(ncm);

            var ex = Assert.Throws<InvalidOperationException>(
                () => NFeItemFiscalResolver.Resolve(product, Settings(), isInterstate: false));

            Assert.Contains("Ncm", ex.Message);
            Assert.Contains("Widget", ex.Message);
            Assert.Contains(product.Id.ToString(), ex.Message);
        }

        [Fact]
        public void Resolve_Crt3WithoutCstAnywhere_Throws()
        {
            var settings = Settings();
            settings.DefaultCst = null;

            var ex = Assert.Throws<InvalidOperationException>(
                () => NFeItemFiscalResolver.Resolve(Product(), settings, isInterstate: false));

            Assert.Contains("DefaultCst", ex.Message);
        }

        [Fact]
        public void Resolve_SimplesNacionalWithoutCsosnAnywhere_Throws()
        {
            var settings = Settings(crt: 1);
            settings.DefaultCsosn = null;

            var ex = Assert.Throws<InvalidOperationException>(
                () => NFeItemFiscalResolver.Resolve(Product(), settings, isInterstate: false));

            Assert.Contains("DefaultCsosn", ex.Message);
        }

        [Fact]
        public void Resolve_BlankCfopOnBothLevels_Throws()
        {
            var settings = Settings();
            settings.DefaultCfopIntra = "";

            Assert.Throws<InvalidOperationException>(
                () => NFeItemFiscalResolver.Resolve(Product(), settings, isInterstate: false));
        }

        [Fact]
        public void Resolve_UnknownCrt_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => NFeItemFiscalResolver.Resolve(Product(), Settings(crt: 9), isInterstate: false));

            Assert.Contains("CRT 9", ex.Message);
        }
    }
}
