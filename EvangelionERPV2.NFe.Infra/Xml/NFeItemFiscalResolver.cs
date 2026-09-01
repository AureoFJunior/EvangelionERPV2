using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Infra.Xml
{
    /// <summary>
    /// Resolves the fiscal data of one NFe/NFCe item: the Product value when it carries one, the
    /// enterprise default otherwise, a hard error when neither exists.
    ///
    /// The third rung is deliberate. Guessing a CFOP, a CST or an NCM produces a note that SEFAZ
    /// happily authorizes and that is wrong on the tax return — far more expensive than a rejected
    /// issuance. Nothing here is inferred.
    /// </summary>
    public static class NFeItemFiscalResolver
    {
        private const string NoGtin = "SEM GTIN";

        public static ResolvedItemFiscal Resolve(Product product, EnterpriseFiscalSettings settings, bool isInterstate)
        {
            ArgumentNullException.ThrowIfNull(product);
            ArgumentNullException.ThrowIfNull(settings);

            // NCM has no enterprise default: it identifies the goods, not the tax treatment, so
            // there is nothing sane to fall back to.
            var ncm = Trimmed(product.Ncm)
                ?? throw Missing(product, nameof(Product.Ncm), "it has no enterprise-level default");

            var cfop = isInterstate
                ? Trimmed(product.CfopInter) ?? Trimmed(settings.DefaultCfopInter)
                : Trimmed(product.CfopIntra) ?? Trimmed(settings.DefaultCfopIntra);

            if (cfop is null)
            {
                throw Missing(product, isInterstate ? nameof(Product.CfopInter) : nameof(Product.CfopIntra), "the enterprise default is blank too");
            }

            var unidadeTributavel = Trimmed(product.UnidadeTributavel) ?? Trimmed(settings.DefaultUnidadeTributavel)
                ?? throw Missing(product, nameof(Product.UnidadeTributavel), "the enterprise default is blank too");

            var cstPis = Trimmed(product.CstPis) ?? Trimmed(settings.DefaultCstPis)
                ?? throw Missing(product, nameof(Product.CstPis), "the enterprise default is blank too");

            var cstCofins = Trimmed(product.CstCofins) ?? Trimmed(settings.DefaultCstCofins)
                ?? throw Missing(product, nameof(Product.CstCofins), "the enterprise default is blank too");

            // CRT decides which of the two mutually exclusive codes the item carries.
            // 1 = Simples Nacional, 2 = Simples above the sublimit, 4 = MEI -> CSOSN.
            // 3 = Regime Normal -> CST.
            string? cst = null;
            string? csosn = null;

            switch (settings.Crt)
            {
                case 3:
                    cst = Trimmed(product.Cst) ?? Trimmed(settings.DefaultCst)
                        ?? throw Missing(product, nameof(Product.Cst), "the enterprise is Regime Normal (CRT 3) and has no DefaultCst");
                    break;
                case 1 or 2 or 4:
                    csosn = Trimmed(product.Csosn) ?? Trimmed(settings.DefaultCsosn)
                        ?? throw Missing(product, nameof(Product.Csosn), $"the enterprise is Simples Nacional (CRT {settings.Crt}) and has no DefaultCsosn");
                    break;
                default:
                    throw new InvalidOperationException($"Enterprise CRT {settings.Crt} is not a valid Código de Regime Tributário (expected 1, 2, 3 or 4).");
            }

            return new ResolvedItemFiscal(
                Ncm: ncm,
                Cest: Trimmed(product.Cest),
                Cean: Trimmed(product.Cean) ?? NoGtin,
                Cfop: cfop,
                Origem: product.Origem ?? settings.DefaultOrigem,
                UnidadeTributavel: unidadeTributavel,
                Cst: cst,
                Csosn: csosn,
                AliquotaIcms: product.AliquotaIcms ?? settings.DefaultAliquotaIcms,
                CstPis: cstPis,
                AliquotaPis: product.AliquotaPis ?? settings.DefaultAliquotaPis,
                CstCofins: cstCofins,
                AliquotaCofins: product.AliquotaCofins ?? settings.DefaultAliquotaCofins,
                CodigoBeneficioFiscal: Trimmed(product.CodigoBeneficioFiscal),

                // Neither of these has a fallback, and deliberately so. A base reduction belongs to
                // the goods and a Simples credit rate belongs to the company, so there is no second
                // place to look; the CST/CSOSN that needs one refuses to build when it is absent.
                ReducaoBaseIcms: product.ReducaoBaseIcms,
                AliquotaCreditoSn: settings.AliquotaCreditoSn);
        }

        private static string? Trimmed(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static InvalidOperationException Missing(Product product, string field, string why) =>
            new($"Product '{product.Name}' ({product.Id}) is missing the fiscal field {field} and {why}. Fill it on the product or on the enterprise fiscal settings before issuing.");
    }

    /// <summary>Fully resolved per-item fiscal data. Every non-nullable member is guaranteed present.</summary>
    public sealed record ResolvedItemFiscal(
        string Ncm,
        string? Cest,
        string Cean,
        string Cfop,
        byte Origem,
        string UnidadeTributavel,
        string? Cst,
        string? Csosn,
        decimal AliquotaIcms,
        string CstPis,
        decimal AliquotaPis,
        string CstCofins,
        decimal AliquotaCofins,
        string? CodigoBeneficioFiscal,
        decimal? ReducaoBaseIcms = null,
        decimal? AliquotaCreditoSn = null);
}
