using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.NFeModule.Infra.Sefaz;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;

using System.Globalization;

using Unimake.Business.DFe.Servicos;
using Unimake.Business.DFe.Xml.NFe;

namespace EvangelionERPV2.NFeModule.Infra.Xml
{
    /// <summary>
    /// Turns one <see cref="NFeIssueContext"/> into the <c>enviNFe</c> object graph the fiscal
    /// library transmits. Pure and offline: no certificate, no network, no database.
    /// </summary>
    public static class NFeXmlBuilder
    {
        /// <summary>Mandated by the NFe 4.00 manual: in homologação the destinatário carries this exact name.</summary>
        private const string HomologationDestinationName = "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

        private const byte HomologationEnvironment = 2;

        /// <summary>verProc, capped at 20 characters by the 4.00 XSD.</summary>
        private const int MaxProcessVersionLength = 20;

        private const int AamMonthOffset = 2;
        private const int CodigoNumericoOffset = 35;
        private const int CodigoNumericoLength = 8;

        /// <param name="responsibleTechnician">
        /// The <c>infRespTec</c> group, when the installation has been told who wrote the software.
        /// Null omits it, which is what every note did before the values existed.
        /// </param>
        public static EnviNFe Build(NFeIssueContext context, NFeResponsibleTechnician? responsibleTechnician = null)
        {
            ArgumentNullException.ThrowIfNull(context);

            var settings = context.FiscalSettings;
            var items = context.Items;

            if (items.Count == 0)
            {
                throw new InvalidOperationException($"Order {context.Order.Id} has no items; an NFe with no item cannot be issued.");
            }

            if (!NFeAccessKeyBuilder.IsValid(context.AccessKey))
            {
                throw new InvalidOperationException("The allocated access key is not a valid 44-digit chave. Refusing to build a document around it.");
            }

            if (context.ProcessVersion.Length > MaxProcessVersionLength)
            {
                throw new InvalidOperationException(
                    $"verProc is {context.ProcessVersion.Length} characters; the 4.00 schema allows at most {MaxProcessVersionLength}.");
            }

            var model = context.Type == NFeDocumentType.NFCe ? ModeloDFe.NFCe : ModeloDFe.NFe;
            var emission = EmissionInstant(context.AccessKey);

            var infNFe = new InfNFe
            {
                Versao = settings.SchemaVersion,
                Ide = BuildIde(context, model, emission),
                Emit = BuildEmit(settings),
                Dest = BuildDest(context),
                Det = [],
                Transp = new Transp { ModFrete = ModalidadeFrete.SemOcorrenciaTransporte }
            };

            var totals = new RunningTotals();
            var isInterstate = IsInterstate(context);

            for (var index = 0; index < items.Count; index++)
            {
                infNFe.Det.Add(BuildDet(index + 1, items[index], settings, isInterstate, totals));
            }

            infNFe.Total = new Total { ICMSTot = totals.ToIcmsTot() };
            infNFe.Pag = BuildPag(context, totals.ProductValue);

            CrossCheckAccessKey(infNFe, context.AccessKey);

            if (responsibleTechnician != null)
            {
                infNFe.InfRespTec = BuildRespTec(responsibleTechnician);
            }

            return new EnviNFe
            {
                Versao = settings.SchemaVersion,
                IdLote = context.Number,

                IndSinc = SimNao.Sim,
                NFe = [new NFe { InfNFeField = infNFe }]
            };
        }

        private static Ide BuildIde(NFeIssueContext context, ModeloDFe model, DateTimeOffset emission)
        {
            var settings = context.FiscalSettings;
            var accessKey = context.AccessKey;

            var ide = new Ide
            {
                CUF = ToUf(settings.Uf, nameof(EnterpriseFiscalSettings.Uf)),
                CNF = accessKey.Substring(CodigoNumericoOffset, CodigoNumericoLength),
                NatOp = OperationNature(settings),
                Mod = model,
                Serie = ParseIdentity(context.Series, nameof(context.Series)),
                NNF = ParseIdentity(context.Number, nameof(context.Number)),
                DhEmi = emission,
                TpNF = TipoOperacao.Saida,
                IdDest = IsInterstate(context) ? DestinoOperacao.OperacaoInterestadual : DestinoOperacao.OperacaoInterna,
                CMunFG = ParseIbgeMunicipality(settings.CodigoMunicipioIbge),
                TpImp = model == ModeloDFe.NFCe ? FormatoImpressaoDANFE.NFCe : FormatoImpressaoDANFE.NormalRetrato,

                TpEmis = context.IsOfflineContingency ? TipoEmissao.ContingenciaOffLine : TipoEmissao.Normal,
                TpAmb = context.Environment == HomologationEnvironment ? TipoAmbiente.Homologacao : TipoAmbiente.Producao,
                FinNFe = FinalidadeNFe.Normal,
                IndFinal = HasStateRegistration(context.Customer) ? SimNao.Nao : SimNao.Sim,

                // ponytail: modelo 55 is issued from a back-office order, never over a counter, so
                // "não presencial, outros" is the honest answer; NFCe is presencial by definition.
                // Revisit when the order gains a sales-channel field.
                IndPres = model == ModeloDFe.NFCe ? IndicadorPresenca.OperacaoPresencial : IndicadorPresenca.OperacaoOutros,
                ProcEmi = ProcessoEmissao.AplicativoContribuinte,
                VerProc = context.ProcessVersion
            };

            if (context.IsOfflineContingency)
            {
                ide.DhCont = ToOffset(context.ContingencyEnteredAt);
                ide.XJust = ContingencyJustification(context);
            }

            return ide;
        }

        /// <summary>
        /// xJust, bounded by the schema at 15..256. Refused here rather than at SEFAZ: a note
        /// rejected for its justification was printed and handed over at a counter minutes ago.
        /// </summary>
        private static string ContingencyJustification(NFeIssueContext context)
        {
            var justification = (context.ContingencyJustification ?? string.Empty).Trim();

            if (justification.Length is < 15 or > 256)
            {
                throw new InvalidOperationException(
                    $"A contingency note needs an xJust of 15 to 256 characters; got {justification.Length}.");
            }

            return justification;
        }

        private static DateTimeOffset ToOffset(DateTime? value)
        {
            if (value is null)
                throw new InvalidOperationException("A contingency note needs dhCont — when the shop entered contingency.");

            return new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                .ToOffset(TimeSpan.FromHours(-3));
        }

        /// <summary>
        /// natOp — the tenant's wording, falling back to the one this was hard-coded to.
        /// </summary>
        private static string OperationNature(EnterpriseFiscalSettings settings)
        {
            var natOp = (settings.DefaultNatOp ?? string.Empty).Trim();

            if (natOp.Length == 0)
                return DefaultOperationNature;

            return natOp.Length > MaxOperationNatureLength ? natOp[..MaxOperationNatureLength] : natOp;
        }

        private const string DefaultOperationNature = "VENDA DE MERCADORIA";

        /// <summary>natOp is 1..60 in the 4.00 schema.</summary>
        private const int MaxOperationNatureLength = 60;

        private static Emit BuildEmit(EnterpriseFiscalSettings settings)
        {
            var cnpj = DocumentValidationHelper.NormalizeDocument(settings.Cnpj);
            if (cnpj.Length != 14)
            {
                throw new InvalidOperationException("The enterprise fiscal profile has no valid 14-digit CNPJ. Complete it before issuing.");
            }

            return new Emit
            {
                CNPJ = cnpj,
                XNome = Required(settings.RazaoSocial, "the enterprise fiscal profile's razão social"),
                XFant = NullIfBlank(settings.NomeFantasia),
                IE = Required(settings.InscricaoEstadual, "the enterprise fiscal profile's inscrição estadual"),
                IM = NullIfBlank(settings.InscricaoMunicipal),
                CRT = ToCrt(settings.Crt),
                EnderEmit = new EnderEmit
                {
                    XLgr = Required(settings.Logradouro, "the enterprise fiscal profile's logradouro"),
                    Nro = Required(settings.Numero, "the enterprise fiscal profile's número"),
                    XCpl = NullIfBlank(settings.Complemento),
                    XBairro = Required(settings.Bairro, "the enterprise fiscal profile's bairro"),
                    CMun = ParseIbgeMunicipality(settings.CodigoMunicipioIbge),
                    XMun = Required(settings.Municipio, "the enterprise fiscal profile's município"),
                    UF = ToUf(settings.Uf, nameof(EnterpriseFiscalSettings.Uf)),
                    CEP = RequiredDigits(settings.Cep, 8, "the enterprise fiscal profile's CEP"),
                    CPais = int.Parse(settings.CodigoPais, CultureInfo.InvariantCulture),
                    XPais = settings.Pais,
                    Fone = NullIfBlank(settings.Telefone)
                }
            };
        }

        private static Dest? BuildDest(NFeIssueContext context)
        {
            var customer = context.Customer;

            if (customer is null)
            {
                if (context.Type != NFeDocumentType.NFCe)
                    throw new InvalidOperationException($"Order {context.Order.Id} has no customer; an NFe needs an identified destinatário.");

                if (context.Order.TotalValue >= context.ConsumerIdentificationThreshold)
                {
                    throw new InvalidOperationException(
                        $"An NFCe of {context.Order.TotalValue:0.00} is at or above the consumer-identification threshold of " +
                        $"{context.ConsumerIdentificationThreshold:0.00}; the consumer's CPF or CNPJ is required.");
                }

                return null;
            }

            var document = DocumentValidationHelper.NormalizeDocument(customer.Document ?? string.Empty);
            var dest = new Dest
            {
                XNome = context.Environment == HomologationEnvironment
                    ? HomologationDestinationName
                    : Required(customer.Name, "the customer's name"),
                Email = NullIfBlank(customer.Email),
                EnderDest = BuildEnderDest(customer),
                IndIEDest = HasStateRegistration(customer)
                    ? IndicadorIEDestinatario.ContribuinteICMS
                    : IndicadorIEDestinatario.NaoContribuinte
            };

            switch (document.Length)
            {
                case 11:
                    dest.CPF = document;
                    break;
                case 14:
                    dest.CNPJ = document;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Customer '{customer.Name}' ({customer.Id}) needs a CPF or CNPJ before an NFe can be issued for them.");
            }

            if (dest.IndIEDest == IndicadorIEDestinatario.ContribuinteICMS)
            {
                dest.IE = customer.InscricaoEstadual!.Trim();
            }

            return dest;
        }

        private static EnderDest BuildEnderDest(Customer customer)
        {
            const string Incomplete = "the customer's fiscal address is incomplete";

            return new EnderDest
            {
                XLgr = Required(customer.Logradouro, $"{Incomplete}: logradouro"),
                Nro = Required(customer.Numero, $"{Incomplete}: número"),
                XCpl = NullIfBlank(customer.Complemento),
                XBairro = Required(customer.Bairro, $"{Incomplete}: bairro"),
                CMun = ParseIbgeMunicipality(customer.CodigoMunicipioIbge),
                XMun = Required(customer.Municipio, $"{Incomplete}: município"),
                UF = ToUf(customer.Uf, $"{Incomplete}: UF"),
                CEP = RequiredDigits(customer.Cep, 8, $"{Incomplete}: CEP"),
                CPais = 1058,
                XPais = "BRASIL",
                Fone = NullIfBlank(customer.PhoneNumber)
            };
        }

        private static Det BuildDet(
            int itemNumber,
            OrderedProduct item,
            EnterpriseFiscalSettings settings,
            bool isInterstate,
            RunningTotals totals)
        {
            var product = item.Product
                ?? throw new InvalidOperationException($"Ordered product {item.Id} was loaded without its Product; the fiscal data cannot be resolved.");

            var fiscal = NFeItemFiscalResolver.Resolve(product, settings, isInterstate);
            var lineValue = Math.Round(item.Quantity * item.Value, 2, MidpointRounding.AwayFromZero);

            var det = new Det
            {
                NItem = itemNumber,
                Prod = new Prod
                {
                    CProd = product.Id.ToString("N"),
                    CEAN = fiscal.Cean,
                    CEANTrib = fiscal.Cean,
                    XProd = Required(product.Name, "the product's name"),
                    NCM = fiscal.Ncm,
                    CEST = fiscal.Cest,
                    CBenef = fiscal.CodigoBeneficioFiscal,
                    CFOP = fiscal.Cfop,
                    UCom = item.UnitOfMeasure,
                    QCom = item.Quantity,
                    VUnCom = item.Value,
                    VProd = (double)lineValue,
                    UTrib = fiscal.UnidadeTributavel,
                    QTrib = item.Quantity,
                    VUnTrib = item.Value,
                    IndTot = SimNao.Sim
                },
                Imposto = new Imposto
                {
                    ICMS = BuildIcms(fiscal, settings, lineValue, totals),
                    PIS = BuildPis(fiscal, lineValue, totals),
                    COFINS = BuildCofins(fiscal, lineValue, totals)
                }
            };

            totals.ProductValue += lineValue;
            return det;
        }

        private static ICMS BuildIcms(ResolvedItemFiscal fiscal, EnterpriseFiscalSettings settings, decimal lineValue, RunningTotals totals)
        {
            var origem = (OrigemMercadoria)fiscal.Origem;

            if (fiscal.Csosn is not null)
            {
                return fiscal.Csosn switch
                {
                    "102" or "103" or "300" or "400" => new ICMS { ICMSSN102 = new ICMSSN102 { Orig = origem, CSOSN = fiscal.Csosn } },
                    "101" => new ICMS { ICMSSN101 = BuildCsosn101(fiscal, origem, lineValue) },

                    // Already taxed by ST upstream, so this note declares no ICMS of its own.
                    // ponytail: the retained-ST figures (vBCSTRet, vICMSSTRet) are optional in the
                    // schema and are not stored anywhere here; a UF that demands them on resale
                    // needs those columns on Product before this can send them.
                    "500" => new ICMS { ICMSSN500 = new ICMSSN500 { Orig = origem, CSOSN = fiscal.Csosn } },
                    _ => throw Unsupported("CSOSN", fiscal.Csosn)
                };
            }

            switch (fiscal.Cst)
            {
                case "00":
                    var tax = Math.Round(lineValue * fiscal.AliquotaIcms, 2, MidpointRounding.AwayFromZero);

                    totals.IcmsBase += lineValue;
                    totals.Icms += tax;

                    return new ICMS
                    {
                        ICMS00 = new ICMS00
                        {
                            Orig = origem,
                            CST = fiscal.Cst,
                            ModBC = ModalidadeBaseCalculoICMS.ValorOperacao,
                            VBC = (double)lineValue,
                            PICMS = Percentage(fiscal.AliquotaIcms),
                            VICMS = (double)tax
                        }
                    };

                case "20":
                    return new ICMS { ICMS20 = BuildIcms20(fiscal, origem, lineValue, totals) };

                case "40" or "41" or "50":
                    return new ICMS { ICMS40 = new ICMS40 { Orig = origem, CST = fiscal.Cst } };

                case "60":
                    return new ICMS { ICMS60 = new ICMS60 { Orig = origem, CST = fiscal.Cst } };

                default:
                    throw Unsupported("ICMS CST", fiscal.Cst ?? "(none)");
            }
        }

        /// <summary>CST 20 — the base is reduced before the rate is applied to it.</summary>
        private static ICMS20 BuildIcms20(ResolvedItemFiscal fiscal, OrigemMercadoria origem, decimal lineValue, RunningTotals totals)
        {
            var reduction = fiscal.ReducaoBaseIcms
                ?? throw new InvalidOperationException(
                    "ICMS CST 20 reduces the calculation base, and the product carries no ReducaoBaseIcms. " +
                    "Fill it on the product — a reduction of zero would declare the full base and overstate the tax.");

            if (reduction <= 0m || reduction >= 1m)
            {
                throw new InvalidOperationException(
                    $"ReducaoBaseIcms is {reduction}, which is not a base reduction. It is a fraction between 0 and 1 " +
                    "(0.2667 is 26,67%); zero means CST 00 and one means the item is exempt, which is another CST.");
            }

            var reducedBase = Math.Round(lineValue * (1m - reduction), 2, MidpointRounding.AwayFromZero);
            var tax = Math.Round(reducedBase * fiscal.AliquotaIcms, 2, MidpointRounding.AwayFromZero);

            totals.IcmsBase += reducedBase;
            totals.Icms += tax;

            return new ICMS20
            {
                Orig = origem,
                CST = fiscal.Cst,
                ModBC = ModalidadeBaseCalculoICMS.ValorOperacao,
                PRedBC = Percentage(reduction),
                VBC = (double)reducedBase,
                PICMS = Percentage(fiscal.AliquotaIcms),
                VICMS = (double)tax
            };
        }

        /// <summary>CSOSN 101 — Simples with a credit the buyer may take.</summary>
        private static ICMSSN101 BuildCsosn101(ResolvedItemFiscal fiscal, OrigemMercadoria origem, decimal lineValue)
        {
            var rate = fiscal.AliquotaCreditoSn
                ?? throw new InvalidOperationException(
                    "CSOSN 101 passes an ICMS credit to the buyer, and the enterprise fiscal profile carries no " +
                    "AliquotaCreditoSn. Fill it — a credit of zero silently denies the buyer a credit they are entitled to.");

            if (rate <= 0m || rate >= 1m)
            {
                throw new InvalidOperationException(
                    $"AliquotaCreditoSn is {rate}, which is not a Simples credit rate. It is a fraction between 0 and 1 " +
                    "(0.0254 is 2,54%).");
            }

            return new ICMSSN101
            {
                Orig = origem,
                CSOSN = fiscal.Csosn,
                PCredSN = Percentage(rate),
                VCredICMSSN = (double)Math.Round(lineValue * rate, 2, MidpointRounding.AwayFromZero)
            };
        }

        private static PIS BuildPis(ResolvedItemFiscal fiscal, decimal lineValue, RunningTotals totals)
        {
            switch (PisCofinsRegimeOf(fiscal.CstPis, "PIS"))
            {
                case ContributionRegime.Aliquota:
                    var tax = Math.Round(lineValue * fiscal.AliquotaPis, 2, MidpointRounding.AwayFromZero);
                    totals.Pis += tax;
                    return new PIS
                    {
                        PISAliq = new PISAliq
                        {
                            CST = fiscal.CstPis,
                            VBC = (double)lineValue,
                            PPIS = Percentage(fiscal.AliquotaPis),
                            VPIS = (double)tax
                        }
                    };

                case ContributionRegime.NaoTributada:
                    return new PIS { PISNT = new PISNT { CST = fiscal.CstPis } };

                default:
                    var other = Math.Round(lineValue * fiscal.AliquotaPis, 2, MidpointRounding.AwayFromZero);
                    totals.Pis += other;
                    return new PIS
                    {
                        PISOutr = new PISOutr
                        {
                            CST = fiscal.CstPis,
                            VBC = (double)lineValue,
                            PPIS = Percentage(fiscal.AliquotaPis),
                            VPIS = (double)other
                        }
                    };
            }
        }

        private static COFINS BuildCofins(ResolvedItemFiscal fiscal, decimal lineValue, RunningTotals totals)
        {
            switch (PisCofinsRegimeOf(fiscal.CstCofins, "COFINS"))
            {
                case ContributionRegime.Aliquota:
                    var tax = Math.Round(lineValue * fiscal.AliquotaCofins, 2, MidpointRounding.AwayFromZero);
                    totals.Cofins += tax;
                    return new COFINS
                    {
                        COFINSAliq = new COFINSAliq
                        {
                            CST = fiscal.CstCofins,
                            VBC = (double)lineValue,
                            PCOFINS = Percentage(fiscal.AliquotaCofins),
                            VCOFINS = (double)tax
                        }
                    };

                case ContributionRegime.NaoTributada:
                    return new COFINS { COFINSNT = new COFINSNT { CST = fiscal.CstCofins } };

                default:
                    var other = Math.Round(lineValue * fiscal.AliquotaCofins, 2, MidpointRounding.AwayFromZero);
                    totals.Cofins += other;
                    return new COFINS
                    {
                        COFINSOutr = new COFINSOutr
                        {
                            CST = fiscal.CstCofins,
                            VBC = (double)lineValue,
                            PCOFINS = Percentage(fiscal.AliquotaCofins),
                            VCOFINS = (double)other
                        }
                    };
            }
        }

        private enum ContributionRegime
        {
            Aliquota,
            NaoTributada,
            Outras
        }

        /// <summary>
        /// Which of the mutually exclusive PIS/COFINS groups a CST belongs to. CST 03 (alíquota por
        /// quantidade) is rejected rather than approximated: it needs a quantity base this system
        /// does not carry.
        /// </summary>
        private static ContributionRegime PisCofinsRegimeOf(string cst, string tax) => cst switch
        {
            "01" or "02" => ContributionRegime.Aliquota,
            "04" or "05" or "06" or "07" or "08" or "09" => ContributionRegime.NaoTributada,
            "03" => throw Unsupported($"{tax} CST", cst),
            _ => ContributionRegime.Outras
        };

        private static Pag BuildPag(NFeIssueContext context, decimal total)
        {
            var tenders = context.Payments;

            if (tenders is null || tenders.Count == 0)
            {
                // ponytail: a back-office order records no payment method, so the document says
                // "outros" rather than asserting a means of payment nobody entered. A till sale
                // does know, and takes the branch below.
                var paidUpFront = context.Order.Payday.HasValue
                    && context.Order.Payday.Value.Date <= DateTime.UtcNow.Date;

                return new Pag
                {
                    DetPag =
                    [
                        new DetPag
                        {
                            IndPag = paidUpFront ? IndicadorPagamento.PagamentoVista : IndicadorPagamento.PagamentoPrazo,
                            TPag = MeioPagamento.Outros,
                            XPag = "OUTROS",
                            VPag = (double)Round(total)
                        }
                    ]
                };
            }

            var settled = tenders.Sum(x => x.Amount);
            var change = tenders.Sum(x => x.ChangeGiven);

            if (Round(settled) != Round(total))
            {
                throw new InvalidOperationException(
                    $"The sale's tenders settle {Round(settled)} but the note totals {Round(total)}; " +
                    "a fiscal document cannot claim a payment that does not add up.");
            }

            return new Pag
            {
                DetPag = tenders.Select(BuildDetPag).ToList(),
                VTroco = (double)Round(change)
            };
        }

        /// <summary>One tender. The mapping to tPag is a lookup, not a judgement — see EnumPaymentMethod.</summary>
        private static DetPag BuildDetPag(PdvSalePayment payment)
        {
            var method = (EnumPaymentMethod)payment.Method;

            var detPag = new DetPag
            {
                IndPag = method is EnumPaymentMethod.StoreCredit or EnumPaymentMethod.Boleto
                    || (method == EnumPaymentMethod.Credit && payment.Installments > 1)
                        ? IndicadorPagamento.PagamentoPrazo
                        : IndicadorPagamento.PagamentoVista,

                TPag = TPagFor(method),
                VPag = (double)Round(payment.Amount + payment.ChangeGiven)
            };

            if (detPag.TPag == MeioPagamento.Outros)
                detPag.XPag = "OUTROS";

            if (method is EnumPaymentMethod.Credit or EnumPaymentMethod.Debit)
            {
                detPag.Card = new Card
                {
                    TpIntegra = TipoIntegracaoPagamento.PagamentoNaoIntegrado,
                    CAut = string.IsNullOrWhiteSpace(payment.AuthorizationCode)
                        ? null
                        : payment.AuthorizationCode.Trim()
                };
            }

            return detPag;
        }

        /// <summary>
        /// The till's vocabulary is the tPag table collapsed to what a counter offers, so this is a
        /// total function with no default that guesses.
        /// </summary>
        private static MeioPagamento TPagFor(EnumPaymentMethod method) => method switch
        {
            EnumPaymentMethod.Cash => MeioPagamento.Dinheiro,
            EnumPaymentMethod.Debit => MeioPagamento.CartaoDebito,
            EnumPaymentMethod.Credit => MeioPagamento.CartaoCredito,
            EnumPaymentMethod.Pix => MeioPagamento.PagamentoInstantaneo,
            EnumPaymentMethod.Voucher => MeioPagamento.ValeAlimentacao,
            EnumPaymentMethod.StoreCredit => MeioPagamento.CreditoLoja,
            EnumPaymentMethod.Boleto => MeioPagamento.BoletoBancario,
            _ => MeioPagamento.Outros
        };

        private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        /// <summary>
        /// dhEmi in the emitter's own timezone, cross-checked against the AAMM already baked into
        /// the chave.
        /// </summary>
        private static DateTimeOffset EmissionInstant(string accessKey)
        {
            // ponytail: one national offset. Brazil has had no DST since 2019 and the v1 UF
            // allowlist (SP/PR/SC/RS) is entirely UTC-3; take the UF's own zone if it grows westward.
            var offset = TimeSpan.FromHours(-3);
            DateTimeOffset now;

            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
                now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                now = DateTimeOffset.UtcNow.ToOffset(offset);
            }

            var competencia = now.ToString("yyMM", CultureInfo.InvariantCulture);
            var chaveCompetencia = accessKey.Substring(AamMonthOffset, 4);

            if (competencia != chaveCompetencia)
            {
                throw new InvalidOperationException(
                    $"The allocated chave carries competência {chaveCompetencia} but the emitter's clock now reads {competencia}. " +
                    "Re-allocate the number before transmitting; a divergent dhEmi is rejected and burns it.");
            }

            return now;
        }

        /// <summary>
        /// Refuses to hand over a document whose chave is not the one already committed.
        /// </summary>
        /// <summary>
        /// <c>infRespTec</c>. The CSRT is handed over raw on purpose: the library derives
        /// <c>hashCSRT</c> from it and the chave, and a hash computed here would be a second
        /// implementation of a rule the fisco already fixed.
        /// </summary>
        private static InfRespTec BuildRespTec(NFeResponsibleTechnician technician)
        {
            var respTec = new InfRespTec
            {
                CNPJ = technician.Cnpj,
                XContato = technician.ContactName,
                Email = technician.Email,
                Fone = technician.Phone
            };

            if (!string.IsNullOrWhiteSpace(technician.IdCsrt) && !string.IsNullOrWhiteSpace(technician.Csrt))
            {
                respTec.IdCSRT = technician.IdCsrt;
                respTec.CSRT = technician.Csrt;
            }

            return respTec;
        }

        private static void CrossCheckAccessKey(InfNFe infNFe, string expected)
        {
            var built = infNFe.Chave;
            if (!string.Equals(built, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The chave derived from the document does not match the one allocated and committed before " +
                    "transmission. The fiscal profile and the allocation disagree; refusing to transmit.");
            }
        }

        private static bool IsInterstate(NFeIssueContext context)
        {
            var destinationUf = NullIfBlank(context.Customer?.Uf);
            return destinationUf is not null
                && !string.Equals(destinationUf, context.FiscalSettings.Uf?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasStateRegistration(Customer? customer) =>
            NullIfBlank(customer?.InscricaoEstadual) is not null;

        private static double Percentage(decimal rate) =>
            (double)Math.Round(rate * 100m, 4, MidpointRounding.AwayFromZero);

        private static UFBrasil ToUf(string? uf, string what)
        {
            var trimmed = NullIfBlank(uf)
                ?? throw new InvalidOperationException($"{what} is blank; an NFe cannot be built without it.");

            return Enum.TryParse<UFBrasil>(trimmed.ToUpperInvariant(), out var parsed)
                ? parsed
                : throw new InvalidOperationException($"{what} is '{trimmed}', which is not a Brazilian UF.");
        }

        private static CRT ToCrt(byte crt) =>
            Enum.IsDefined(typeof(CRT), (int)crt)
                ? (CRT)crt
                : throw new InvalidOperationException($"Enterprise CRT {crt} is not a valid Código de Regime Tributário (expected 1, 2, 3 or 4).");

        private static int ParseIbgeMunicipality(string? code)
        {
            var digits = RequiredDigits(code, 7, "the IBGE municipality code");
            return int.Parse(digits, CultureInfo.InvariantCulture);
        }

        private static int ParseIdentity(string value, string what) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"{what} is '{value}', which is not a number.");

        private static string Required(string? value, string what) =>
            NullIfBlank(value) ?? throw new InvalidOperationException($"{what} is blank; an NFe cannot be built without it.");

        private static string RequiredDigits(string? value, int length, string what)
        {
            var digits = new string((NullIfBlank(value) ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
            return digits.Length == length
                ? digits
                : throw new InvalidOperationException($"{what} must be exactly {length} digits; got '{value}'.");
        }

        private static string? NullIfBlank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static InvalidOperationException Unsupported(string what, string value) =>
            new($"{what} '{value}' is not supported by this issuer. Supported today: ICMS CST 00, 20, 40, 41, 50 and 60, " +
                "CSOSN 101, 102, 103, 300, 400 and 500, and PIS/COFINS CSTs other than 03. Extending this needs the tax " +
                "fields the code requires — a deferment percentage, an ST base and margin — not a default of zero.");

        /// <summary>Accumulates the note-level totals as items are built, so nothing is summed twice.</summary>
        private sealed class RunningTotals
        {
            public decimal ProductValue;
            public decimal IcmsBase;
            public decimal Icms;
            public decimal Pis;
            public decimal Cofins;

            public ICMSTot ToIcmsTot() => new()
            {
                VBC = (double)IcmsBase,
                VICMS = (double)Icms,
                VProd = (double)ProductValue,
                VPIS = (double)Pis,
                VCOFINS = (double)Cofins,
                VNF = (double)ProductValue
            };
        }
    }
}
