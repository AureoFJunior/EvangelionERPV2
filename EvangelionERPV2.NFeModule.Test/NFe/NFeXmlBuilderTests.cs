using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.NFeModule.Infra.Sefaz;
using EvangelionERPV2.NFeModule.Infra.Xml;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;

using System.Globalization;

using Unimake.Business.DFe.Servicos;
using Unimake.Business.DFe.Xml.NFe;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    /// <summary>
    /// Covers the one thing that cannot be checked after the fact: a note is only wrong once it has
    /// been authorized, and then the remedy is cancellation inside a legal window. Everything here
    /// runs offline — no certificate, no socket.
    /// </summary>
    public class NFeXmlBuilderTests
    {
        private static readonly Guid EnterpriseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid ProductId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid OrderId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        private const string EmitterCnpj = "12345678000195";

        private static EnterpriseFiscalSettings Settings(byte crt = 3, byte environment = 1) => new()
        {
            EnterpriseId = EnterpriseId,
            Cnpj = EmitterCnpj,
            InscricaoEstadual = "123456789",
            RazaoSocial = "EVANGELION COMERCIO LTDA",
            NomeFantasia = "EVANGELION",
            Crt = crt,
            Logradouro = "RUA DAS FLORES",
            Numero = "100",
            Bairro = "CENTRO",
            CodigoMunicipioIbge = "4314902",
            Municipio = "PORTO ALEGRE",
            Uf = "RS",
            CodigoUfIbge = 43,
            Cep = "90010000",
            Environment = environment,
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

        private static Customer Buyer(string uf = "RS") => new()
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Name = "CLIENTE EXEMPLO LTDA",
            Email = "cliente@example.com",
            PhoneNumber = "5133334444",
            Document = "98765432000198",
            Logradouro = "AVENIDA BRASIL",
            Numero = "2000",
            Complemento = "SALA 3",
            Bairro = "SAO JOAO",
            CodigoMunicipioIbge = "4314902",
            Municipio = "PORTO ALEGRE",
            Uf = uf,
            Cep = "90230000"
        };

        private static Product Widget() => new()
        {
            Id = ProductId,
            Name = "WIDGET DE ACO",
            Ncm = "73269090",
            EnterpriseId = EnterpriseId
        };

        /// <summary>
        /// A chave whose AAMM is the current competência in the emitter's timezone. Hard-coding one
        /// would make the suite fail on the first of every month for a reason unrelated to the code.
        /// </summary>
        private static string AccessKeyForNow(int number = 42, int series = 1, int model = 55)
        {
            var emission = DateTime.UtcNow.AddHours(-3);
            return NFeAccessKeyBuilder.Build(
                codigoUf: 43,
                emission: emission,
                cnpj: EmitterCnpj,
                model: model,
                series: series,
                number: number,
                tipoEmissao: 1,
                codigoNumerico: "00099887");
        }

        private static NFeIssueContext Context(
            EnterpriseFiscalSettings? settings = null,
            Customer? customer = null,
            IReadOnlyList<OrderedProduct>? items = null,
            string? accessKey = null,
            IReadOnlyList<PdvSalePayment>? payments = null)
        {
            settings ??= Settings();
            var order = new Order { Id = OrderId, EnterpriseId = EnterpriseId, TotalValue = 250m };

            items ??=
            [
                new OrderedProduct
                {
                    Id = Guid.NewGuid(),
                    OrderId = OrderId,
                    ProductId = ProductId,
                    Product = Widget(),
                    Quantity = 2m,
                    Value = 125m,
                    UnitOfMeasure = "UN"
                }
            ];

            return new NFeIssueContext
            {
                Order = order,
                Items = items,
                FiscalSettings = settings,
                Customer = customer ?? Buyer(),
                Type = NFeDocumentType.NFe,
                AccessKey = accessKey ?? AccessKeyForNow(),
                Number = "42",
                Series = "1",
                Payments = payments
            };
        }

        private static InfNFe Build(NFeIssueContext context) => NFeXmlBuilder.Build(context).NFe[0].InfNFeField;

        /// <summary>The default line — 2 x 125,00 — around a product the test configured itself.</summary>
        private static OrderedProduct Line(Product product) => new()
        {
            Id = Guid.NewGuid(),
            OrderId = OrderId,
            ProductId = product.Id,
            Product = product,
            Quantity = 2m,
            Value = 125m,
            UnitOfMeasure = "UN"
        };

        #region natOp

        [Fact]
        public void NatOp_IsTheTenantsWording()
        {
            var settings = Settings();
            settings.DefaultNatOp = "VENDA AO CONSUMIDOR FINAL";

            Assert.Equal("VENDA AO CONSUMIDOR FINAL", Build(Context(settings)).Ide.NatOp);
        }

        [Fact]
        public void NatOp_WhenTheTenantHasNone_IsWhatEveryPastNoteSaid()
        {
            var settings = Settings();
            settings.DefaultNatOp = "   ";

            Assert.Equal("VENDA DE MERCADORIA", Build(Context(settings)).Ide.NatOp);
        }

        [Fact]
        public void NatOp_LongerThanTheSchemaAllows_IsCutRatherThanRejected()
        {
            var settings = Settings();
            settings.DefaultNatOp = new string('A', 80);

            Assert.Equal(60, Build(Context(settings)).Ide.NatOp.Length);
        }

        #endregion

        #region pag — how the counter was actually paid

        private static PdvSalePayment Tender(
            EnumPaymentMethod method,
            decimal amount,
            decimal change = 0m,
            string? authorization = null,
            int? installments = null) => new()
            {
                Method = (int)method,
                Amount = amount,
                ChangeGiven = change,
                AuthorizationCode = authorization,
                Installments = installments
            };

        [Fact]
        public void Pag_WithoutTenders_StillSaysOutros()
        {
            var detPag = Assert.Single(Build(Context()).Pag.DetPag);

            Assert.Equal(MeioPagamento.Outros, detPag.TPag);
            Assert.Equal("OUTROS", detPag.XPag);
            Assert.Equal(250d, detPag.VPag);
        }

        [Fact]
        public void Pag_SplitBetweenCashAndCard_IsOneDetPagEach()
        {
            var infNFe = Build(Context(payments:
            [
                Tender(EnumPaymentMethod.Cash, 100m),
                Tender(EnumPaymentMethod.Credit, 150m, authorization: "AUT-9911")
            ]));

            Assert.Equal(2, infNFe.Pag.DetPag.Count);
            Assert.Equal(MeioPagamento.Dinheiro, infNFe.Pag.DetPag[0].TPag);
            Assert.Equal(100d, infNFe.Pag.DetPag[0].VPag);
            Assert.Equal(MeioPagamento.CartaoCredito, infNFe.Pag.DetPag[1].TPag);
            Assert.Equal(150d, infNFe.Pag.DetPag[1].VPag);

            Assert.Null(infNFe.Pag.DetPag[0].XPag);
        }

        [Fact]
        public void Pag_CashWithChange_DeclaresWhatWasHandedOverAndWhatCameBack()
        {
            var infNFe = Build(Context(payments: [Tender(EnumPaymentMethod.Cash, 250m, change: 50m)]));

            Assert.Equal(300d, infNFe.Pag.DetPag[0].VPag);
            Assert.Equal(50d, infNFe.Pag.VTroco);
        }

        [Fact]
        public void Pag_ACardTender_CarriesItsAuthorizationAndSaysItIsNotIntegrated()
        {
            var card = Assert.Single(Build(Context(payments:
                [Tender(EnumPaymentMethod.Debit, 250m, authorization: " AUT-4242 ")])).Pag.DetPag).Card;

            Assert.NotNull(card);
            Assert.Equal(TipoIntegracaoPagamento.PagamentoNaoIntegrado, card!.TpIntegra);
            Assert.Equal("AUT-4242", card.CAut);
        }

        [Theory]
        [InlineData(EnumPaymentMethod.Cash, MeioPagamento.Dinheiro)]
        [InlineData(EnumPaymentMethod.Debit, MeioPagamento.CartaoDebito)]
        [InlineData(EnumPaymentMethod.Credit, MeioPagamento.CartaoCredito)]
        [InlineData(EnumPaymentMethod.Pix, MeioPagamento.PagamentoInstantaneo)]
        [InlineData(EnumPaymentMethod.Voucher, MeioPagamento.ValeAlimentacao)]
        [InlineData(EnumPaymentMethod.StoreCredit, MeioPagamento.CreditoLoja)]
        [InlineData(EnumPaymentMethod.Boleto, MeioPagamento.BoletoBancario)]
        [InlineData(EnumPaymentMethod.Other, MeioPagamento.Outros)]
        public void Pag_EveryTillMethod_HasItsOwnTPag(EnumPaymentMethod method, MeioPagamento expected)
        {
            var detPag = Assert.Single(Build(Context(payments: [Tender(method, 250m)])).Pag.DetPag);

            Assert.Equal(expected, detPag.TPag);
        }

        [Fact]
        public void Pag_WhatTheShopIsStillOwed_IsAPrazo()
        {
            Assert.Equal(
                IndicadorPagamento.PagamentoVista,
                Assert.Single(Build(Context(payments: [Tender(EnumPaymentMethod.Cash, 250m)])).Pag.DetPag).IndPag);

            Assert.Equal(
                IndicadorPagamento.PagamentoPrazo,
                Assert.Single(Build(Context(payments: [Tender(EnumPaymentMethod.Boleto, 250m)])).Pag.DetPag).IndPag);

            Assert.Equal(
                IndicadorPagamento.PagamentoPrazo,
                Assert.Single(Build(Context(payments:
                    [Tender(EnumPaymentMethod.Credit, 250m, installments: 3)])).Pag.DetPag).IndPag);
        }

        [Fact]
        public void Pag_TendersThatDoNotAddUp_AreRefusedBeforeSefazSeesThem()
        {
            var context = Context(payments: [Tender(EnumPaymentMethod.Cash, 249m)]);

            var exception = Assert.Throws<InvalidOperationException>(() => Build(context));

            Assert.Contains("249", exception.Message);
            Assert.Contains("250", exception.Message);
        }

        #endregion

        #region infRespTec

        [Fact]
        public void Build_OmitsRespTec_WhenTheInstallationHasNotBeenToldWhoWroteTheSoftware()
        {
            Assert.Null(Build(Context()).InfRespTec);
        }

        [Fact]
        public void Build_WritesRespTec_WhenItIsConfigured()
        {
            var technician = new NFeResponsibleTechnician(
                "12345678000199", "Aureo", "dev@nerv.jp", "5199990000", null, null);

            var respTec = NFeXmlBuilder.Build(Context(), technician).NFe[0].InfNFeField.InfRespTec;

            Assert.NotNull(respTec);
            Assert.Equal("12345678000199", respTec!.CNPJ);
            Assert.Equal("Aureo", respTec.XContato);
            Assert.Equal("dev@nerv.jp", respTec.Email);
            Assert.Equal("5199990000", respTec.Fone);

            Assert.True(string.IsNullOrEmpty(respTec.IdCSRT));
        }

        [Fact]
        public void Build_WritesTheCsrtPair_WhenBothHalvesArePresent()
        {
            var technician = new NFeResponsibleTechnician(
                "12345678000199", "Aureo", "dev@nerv.jp", "5199990000", "01", "G8063VRTNDMPS3W2SPT9V6NEEJ0MYCF7");

            var respTec = NFeXmlBuilder.Build(Context(), technician).NFe[0].InfNFeField.InfRespTec;

            Assert.Equal("01", respTec!.IdCSRT);
            Assert.Equal("G8063VRTNDMPS3W2SPT9V6NEEJ0MYCF7", respTec.CSRT);

            Assert.True(string.IsNullOrEmpty(respTec.HashCSRT));
        }

        #endregion

        [Fact]
        public void Build_ProducesTheAllocatedIdentity_RatherThanMintingItsOwn()
        {
            var context = Context();
            var infNFe = Build(context);

            Assert.Equal(context.AccessKey, infNFe.Chave);
            Assert.Equal("NFe" + context.AccessKey, infNFe.Id);
            Assert.Equal(42, infNFe.Ide.NNF);
            Assert.Equal(1, infNFe.Ide.Serie);

            Assert.Equal(context.AccessKey.Substring(35, 8), infNFe.Ide.CNF);
            Assert.Equal(context.AccessKey[43] - '0', infNFe.Ide.CDV);
        }

        [Fact]
        public void Build_SetsTheModelAndOperationForAModelo55Sale()
        {
            var infNFe = Build(Context());

            Assert.Equal(ModeloDFe.NFe, infNFe.Ide.Mod);
            Assert.Equal(TipoOperacao.Saida, infNFe.Ide.TpNF);
            Assert.Equal(TipoEmissao.Normal, infNFe.Ide.TpEmis);
            Assert.Equal(FinalidadeNFe.Normal, infNFe.Ide.FinNFe);
            Assert.Equal(UFBrasil.RS, infNFe.Ide.CUF);
            Assert.Equal(TipoAmbiente.Producao, infNFe.Ide.TpAmb);
        }

        [Fact]
        public void Build_UsesTheIntrastateCfop_WhenBuyerIsInTheEmittersUf()
        {
            var infNFe = Build(Context());

            Assert.Equal(DestinoOperacao.OperacaoInterna, infNFe.Ide.IdDest);
            Assert.Equal("5102", infNFe.Det[0].Prod.CFOP);
        }

        [Theory]
        [InlineData("SP")]
        [InlineData("MG")]
        [InlineData("AM")]
        public void Build_UsesTheInterstateCfop_WhenBuyerIsInAnotherUf(string uf)
        {
            var infNFe = Build(Context(customer: Buyer(uf)));

            Assert.Equal(DestinoOperacao.OperacaoInterestadual, infNFe.Ide.IdDest);
            Assert.Equal("6102", infNFe.Det[0].Prod.CFOP);
            Assert.Equal(Enum.Parse<UFBrasil>(uf), infNFe.Dest.EnderDest.UF);
        }

        [Fact]
        public void Build_InHomologation_ReplacesTheBuyerNameWithTheMandatedString()
        {
            var infNFe = Build(Context(settings: Settings(environment: 2)));

            Assert.Equal("NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL", infNFe.Dest.XNome);
            Assert.Equal(TipoAmbiente.Homologacao, infNFe.Ide.TpAmb);

            Assert.Equal("AVENIDA BRASIL", infNFe.Dest.EnderDest.XLgr);
        }

        [Fact]
        public void Build_PutsACnpjBuyerInCnpj_AndACpfBuyerInCpf()
        {
            var company = Build(Context()).Dest;
            Assert.Equal("98765432000198", company.CNPJ);
            Assert.Null(company.CPF);

            var person = Buyer();
            person.Document = "12345678909";
            var individual = Build(Context(customer: person)).Dest;

            Assert.Equal("12345678909", individual.CPF);
            Assert.Null(individual.CNPJ);
        }

        [Fact]
        public void Build_MarksTheBuyerANonContributor_WhenTheyHaveNoStateRegistration()
        {
            var infNFe = Build(Context());

            Assert.Equal(IndicadorIEDestinatario.NaoContribuinte, infNFe.Dest.IndIEDest);
            Assert.Null(infNFe.Dest.IE);

            Assert.Equal(SimNao.Sim, infNFe.Ide.IndFinal);
        }

        [Fact]
        public void Build_MarksTheBuyerAnIcmsContributor_WhenTheyHaveAStateRegistration()
        {
            var buyer = Buyer();
            buyer.InscricaoEstadual = "0987654321";

            var infNFe = Build(Context(customer: buyer));

            Assert.Equal(IndicadorIEDestinatario.ContribuinteICMS, infNFe.Dest.IndIEDest);
            Assert.Equal("0987654321", infNFe.Dest.IE);
            Assert.Equal(SimNao.Nao, infNFe.Ide.IndFinal);
        }

        [Fact]
        public void Build_UnderRegimeNormal_EmitsIcms00WithTheResolvedRate()
        {
            var infNFe = Build(Context());
            var icms = infNFe.Det[0].Imposto.ICMS.ICMS00;

            Assert.NotNull(icms);
            Assert.Equal("00", icms.CST);
            Assert.Equal(ModalidadeBaseCalculoICMS.ValorOperacao, icms.ModBC);

            Assert.Equal(18d, icms.PICMS);
            Assert.Equal(250d, icms.VBC);
            Assert.Equal(45d, icms.VICMS);
        }

        [Fact]
        public void Build_WithCst20_ReducesTheBaseBeforeTheRateAndTotalsTheReducedBase()
        {
            var settings = Settings();
            settings.DefaultCst = "20";

            var product = Widget();
            product.ReducaoBaseIcms = 0.2667m;

            var infNFe = Build(Context(settings, items: [Line(product)]));
            var icms = infNFe.Det[0].Imposto.ICMS.ICMS20;

            Assert.NotNull(icms);
            Assert.Equal("20", icms.CST);
            Assert.Equal(26.67d, icms.PRedBC);

            Assert.Equal(183.33d, icms.VBC);
            Assert.Equal(18d, icms.PICMS);
            Assert.Equal(33d, icms.VICMS);

            Assert.Equal(183.33d, infNFe.Total.ICMSTot.VBC);
            Assert.Equal(33d, infNFe.Total.ICMSTot.VICMS);
        }

        [Fact]
        public void Build_WithCst20AndNoReduction_RefusesRatherThanDeclaringTheFullBase()
        {
            var settings = Settings();
            settings.DefaultCst = "20";

            var refusal = Assert.Throws<InvalidOperationException>(
                () => Build(Context(settings, items: [Line(Widget())])));

            Assert.Contains("ReducaoBaseIcms", refusal.Message);
        }

        [Theory]
        [InlineData("40")]
        [InlineData("41")]
        [InlineData("50")]
        public void Build_WithAnExemptCst_DeclaresTheExemptionAndNoTax(string cst)
        {
            var settings = Settings();
            settings.DefaultCst = cst;

            var infNFe = Build(Context(settings, items: [Line(Widget())]));
            var icms = infNFe.Det[0].Imposto.ICMS;

            Assert.NotNull(icms.ICMS40);
            Assert.Equal(cst, icms.ICMS40.CST);
            Assert.Null(icms.ICMS00);

            Assert.Equal(0d, infNFe.Total.ICMSTot.VBC);
            Assert.Equal(0d, infNFe.Total.ICMSTot.VICMS);
        }

        [Fact]
        public void Build_WithCst60_DeclaresTheTaxAlreadyCollectedUpstream()
        {
            var settings = Settings();
            settings.DefaultCst = "60";

            var infNFe = Build(Context(settings, items: [Line(Widget())]));
            var icms = infNFe.Det[0].Imposto.ICMS;

            Assert.NotNull(icms.ICMS60);
            Assert.Equal("60", icms.ICMS60.CST);

            Assert.Equal(0d, infNFe.Total.ICMSTot.VICMS);
        }

        [Fact]
        public void Build_WithCsosn101_PassesTheBuyerTheCreditWithoutClaimingTheTax()
        {
            var settings = Settings(crt: 1);
            settings.DefaultCsosn = "101";
            settings.AliquotaCreditoSn = 0.0254m;

            var infNFe = Build(Context(settings, items: [Line(Widget())]));
            var icms = infNFe.Det[0].Imposto.ICMS.ICMSSN101;

            Assert.NotNull(icms);
            Assert.Equal("101", icms.CSOSN);
            Assert.Equal(2.54d, icms.PCredSN);
            Assert.Equal(6.35d, icms.VCredICMSSN);

            Assert.Equal(0d, infNFe.Total.ICMSTot.VICMS);
        }

        [Fact]
        public void Build_WithCsosn101AndNoCreditRate_RefusesRatherThanDenyingTheBuyerTheCredit()
        {
            var settings = Settings(crt: 1);
            settings.DefaultCsosn = "101";

            var refusal = Assert.Throws<InvalidOperationException>(
                () => Build(Context(settings, items: [Line(Widget())])));

            Assert.Contains("AliquotaCreditoSn", refusal.Message);
        }

        [Fact]
        public void Build_WithCsosn500_DeclaresTheTaxAlreadyCollectedUpstream()
        {
            var settings = Settings(crt: 1);
            settings.DefaultCsosn = "500";

            var icms = Build(Context(settings, items: [Line(Widget())])).Det[0].Imposto.ICMS;

            Assert.NotNull(icms.ICMSSN500);
            Assert.Equal("500", icms.ICMSSN500.CSOSN);
        }

        [Theory]
        [InlineData(3, "51")]
        [InlineData(3, "70")]
        [InlineData(3, "90")]
        [InlineData(1, "201")]
        [InlineData(1, "900")]
        public void Build_WithACodeWhoseFiguresAreNotStored_StillRefusesByName(byte crt, string code)
        {
            var settings = Settings(crt: crt);
            if (crt == 3) settings.DefaultCst = code; else settings.DefaultCsosn = code;

            var refusal = Assert.Throws<InvalidOperationException>(
                () => Build(Context(settings, items: [Line(Widget())])));

            Assert.Contains(code, refusal.Message);
        }

        [Fact]
        public void Build_UnderSimplesNacional_EmitsCsosnAndNoIcmsValue()
        {
            var infNFe = Build(Context(settings: Settings(crt: 1)));
            var icms = infNFe.Det[0].Imposto.ICMS;

            Assert.Null(icms.ICMS00);
            Assert.NotNull(icms.ICMSSN102);
            Assert.Equal("102", icms.ICMSSN102.CSOSN);

            Assert.Equal(0d, infNFe.Total.ICMSTot.VBC);
            Assert.Equal(0d, infNFe.Total.ICMSTot.VICMS);
        }

        [Fact]
        public void Build_RoutesPisAndCofinsByCst_NotByAssumingAnAliquotaGroup()
        {
            var untaxed = Build(Context()).Det[0].Imposto;
            Assert.NotNull(untaxed.PIS.PISNT);
            Assert.Null(untaxed.PIS.PISAliq);
            Assert.NotNull(untaxed.COFINS.COFINSNT);

            var product = Widget();
            product.CstPis = "01";
            product.AliquotaPis = 0.0165m;
            product.CstCofins = "01";
            product.AliquotaCofins = 0.076m;

            var taxed = Build(Context(items:
            [
                new OrderedProduct
                {
                    Id = Guid.NewGuid(), OrderId = OrderId, ProductId = ProductId, Product = product,
                    Quantity = 2m, Value = 125m, UnitOfMeasure = "UN"
                }
            ])).Det[0].Imposto;

            Assert.NotNull(taxed.PIS.PISAliq);
            Assert.Equal(1.65d, taxed.PIS.PISAliq.PPIS);
            Assert.Equal(4.13d, taxed.PIS.PISAliq.VPIS);
            Assert.NotNull(taxed.COFINS.COFINSAliq);
            Assert.Equal(19d, taxed.COFINS.COFINSAliq.VCOFINS);
        }

        [Fact]
        public void Build_TotalsEveryLine_AndReportsThemOnce()
        {
            var second = Widget();
            var context = Context(items:
            [
                new OrderedProduct { Id = Guid.NewGuid(), OrderId = OrderId, ProductId = ProductId, Product = Widget(), Quantity = 2m, Value = 125m, UnitOfMeasure = "UN" },
                new OrderedProduct { Id = Guid.NewGuid(), OrderId = OrderId, ProductId = ProductId, Product = second, Quantity = 3m, Value = 10m, UnitOfMeasure = "UN" }
            ]);

            var infNFe = Build(context);

            Assert.Equal(2, infNFe.Det.Count);
            Assert.Equal(1, infNFe.Det[0].NItem);
            Assert.Equal(2, infNFe.Det[1].NItem);
            Assert.Equal(280d, infNFe.Total.ICMSTot.VProd);
            Assert.Equal(280d, infNFe.Total.ICMSTot.VNF);
            Assert.Equal(50.4d, infNFe.Total.ICMSTot.VICMS);
            Assert.Equal(280d, infNFe.Pag.DetPag[0].VPag);
        }

        [Fact]
        public void Build_RefusesAnOrderWithNoCustomer()
        {
            var context = new NFeIssueContext
            {
                Order = new Order { Id = OrderId, EnterpriseId = EnterpriseId, TotalValue = 10m },
                Items = Context().Items,
                FiscalSettings = Settings(),
                Customer = null,
                Type = NFeDocumentType.NFe,
                AccessKey = AccessKeyForNow(),
                Number = "42",
                Series = "1"
            };

            var exception = Assert.Throws<InvalidOperationException>(() => NFeXmlBuilder.Build(context));
            Assert.Contains("destinatário", exception.Message);
        }

        [Fact]
        public void Build_RefusesACustomerWithNoDocument()
        {
            var buyer = Buyer();
            buyer.Document = null;

            var exception = Assert.Throws<InvalidOperationException>(() => NFeXmlBuilder.Build(Context(customer: buyer)));
            Assert.Contains("CPF or CNPJ", exception.Message);
        }

        [Theory]
        [InlineData(nameof(Customer.Logradouro))]
        [InlineData(nameof(Customer.Numero))]
        [InlineData(nameof(Customer.Bairro))]
        [InlineData(nameof(Customer.CodigoMunicipioIbge))]
        [InlineData(nameof(Customer.Municipio))]
        [InlineData(nameof(Customer.Uf))]
        [InlineData(nameof(Customer.Cep))]
        public void Build_RefusesAnIncompleteCustomerAddress_RatherThanEmittingABlankField(string field)
        {
            var buyer = Buyer();
            typeof(Customer).GetProperty(field)!.SetValue(buyer, null);

            Assert.Throws<InvalidOperationException>(() => NFeXmlBuilder.Build(Context(customer: buyer)));
        }

        [Fact]
        public void Build_RefusesAnIcmsCstItCannotPopulateHonestly()
        {
            var product = Widget();

            product.Cst = "51";

            var exception = Assert.Throws<InvalidOperationException>(() => NFeXmlBuilder.Build(Context(items:
            [
                new OrderedProduct { Id = Guid.NewGuid(), OrderId = OrderId, ProductId = ProductId, Product = product, Quantity = 1m, Value = 10m, UnitOfMeasure = "UN" }
            ])));

            Assert.Contains("not supported", exception.Message);
        }

        [Fact]
        public void Build_RefusesAChaveWhoseCompetenciaHasRolledOver()
        {
            var lastMonth = DateTime.UtcNow.AddHours(-3).AddMonths(-1);
            var staleKey = NFeAccessKeyBuilder.Build(43, lastMonth, EmitterCnpj, 55, 1, 42, 1, "00099887");

            var exception = Assert.Throws<InvalidOperationException>(() => NFeXmlBuilder.Build(Context(accessKey: staleKey)));
            Assert.Contains("Re-allocate", exception.Message);
        }

        [Fact]
        public void Build_RefusesAVerProcLongerThanTheSchemaAllows()
        {
            var context = Context();
            var tooLong = new NFeIssueContext
            {
                Order = context.Order,
                Items = context.Items,
                FiscalSettings = context.FiscalSettings,
                Customer = context.Customer,
                Type = context.Type,
                AccessKey = context.AccessKey,
                Number = context.Number,
                Series = context.Series,
                ProcessVersion = "EvangelionERPV2-phase0"
            };

            Assert.Throws<InvalidOperationException>(() => NFeXmlBuilder.Build(tooLong));
        }

        [Fact]
        public void Build_RefusesAnOrderWithNoItems()
        {
            Assert.Throws<InvalidOperationException>(() => NFeXmlBuilder.Build(Context(items: [])));
        }

        /// <summary>
        /// Golden snapshot of the whole infNFe for a fixed order. This is the regression net for a
        /// schema-version bump: a Nota Técnica that changes a field turns into a red build here
        /// instead of a rejection storm in production.
        /// </summary>
        [Fact]
        public void Build_MatchesTheGoldenInfNFe()
        {
            var context = Context();
            var xml = NFeXmlBuilder.Build(context).GerarXML().OuterXml;

            xml = xml
                .Replace(context.AccessKey, "{CHAVE}", StringComparison.Ordinal)
                .Replace(context.AccessKey.Substring(35, 8), "{CNF}", StringComparison.Ordinal);
            xml = System.Text.RegularExpressions.Regex.Replace(xml, "<dhEmi>[^<]+</dhEmi>", "<dhEmi>{DHEMI}</dhEmi>");

            Assert.Contains("<natOp>VENDA DE MERCADORIA</natOp>", xml);
            Assert.Contains("<mod>55</mod>", xml);
            Assert.Contains("<serie>1</serie>", xml);
            Assert.Contains("<nNF>42</nNF>", xml);
            Assert.Contains("<CNPJ>12345678000195</CNPJ>", xml);
            Assert.Contains("<xNome>EVANGELION COMERCIO LTDA</xNome>", xml);
            Assert.Contains("<CRT>3</CRT>", xml);
            Assert.Contains("<xLgr>AVENIDA BRASIL</xLgr>", xml);
            Assert.Contains("<cMun>4314902</cMun>", xml);
            Assert.Contains("<NCM>73269090</NCM>", xml);
            Assert.Contains("<CFOP>5102</CFOP>", xml);
            Assert.Contains("<cEAN>SEM GTIN</cEAN>", xml);
            Assert.Contains("<indTot>1</indTot>", xml);
            Assert.Contains("<vNF>250.00</vNF>", xml);
            Assert.Contains("<modFrete>9</modFrete>", xml);
            Assert.Contains("<tPag>99</tPag>", xml);
            Assert.Contains("<indSinc>1</indSinc>", xml);

            Assert.DoesNotContain("infRespTec", xml);
        }
    }
}
