using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    /// <summary>
    /// The per-product fiscal field rules, applied identically to the create and update bodies.
    /// </summary>
    /// <remarks>
    /// Front door validation, like <see cref="UpdateFiscalSettingsRequestValidator"/>: the limits
    /// mirror the entity's column widths and the SEFAZ field formats so a malformed NCM is a 400
    /// instead of a rejected transmission. Every rule is conditional on the field being present,
    /// because null means "fall back to the enterprise default".
    /// </remarks>
    internal static class ProductFiscalRules
    {
        public static void AddTo<TRequest>(AbstractValidator<TRequest> validator)
            where TRequest : IProductFiscalFields
        {
            validator.RuleFor(x => x.Ncm)
                .Must(value => HasDigitCount(value, 8)).WithMessage("Ncm must have 8 digits.")
                .When(x => !string.IsNullOrWhiteSpace(x.Ncm));

            validator.RuleFor(x => x.Cest)
                .Must(value => HasDigitCount(value, 7)).WithMessage("Cest must have 7 digits.")
                .When(x => !string.IsNullOrWhiteSpace(x.Cest));

            // GTIN-8/12/13/14 are all legal, so only the column width is enforced here.
            validator.RuleFor(x => x.Cean)
                .MaximumLength(14).WithMessage("Cean is too long.")
                .When(x => !string.IsNullOrWhiteSpace(x.Cean));

            validator.RuleFor(x => x.CfopIntra)
                .Must(value => HasDigitCount(value, 4)).WithMessage("CfopIntra must have 4 digits.")
                .When(x => !string.IsNullOrWhiteSpace(x.CfopIntra));

            validator.RuleFor(x => x.CfopInter)
                .Must(value => HasDigitCount(value, 4)).WithMessage("CfopInter must have 4 digits.")
                .When(x => !string.IsNullOrWhiteSpace(x.CfopInter));

            validator.RuleFor(x => x.Origem)
                .InclusiveBetween((byte)0, (byte)8).WithMessage("Origem must be between 0 and 8.")
                .When(x => x.Origem.HasValue);

            validator.RuleFor(x => x.UnidadeTributavel)
                .MaximumLength(6).WithMessage("UnidadeTributavel is too long.")
                .When(x => !string.IsNullOrWhiteSpace(x.UnidadeTributavel));

            // Cst vs Csosn is picked by the enterprise's CRT, which this request does not carry —
            // NFeItemFiscalResolver decides which one is read. Sending both is allowed.
            validator.RuleFor(x => x.Cst)
                .MaximumLength(3).WithMessage("Cst is too long.")
                .When(x => !string.IsNullOrWhiteSpace(x.Cst));

            validator.RuleFor(x => x.Csosn)
                .MaximumLength(3).WithMessage("Csosn is too long.")
                .When(x => !string.IsNullOrWhiteSpace(x.Csosn));

            validator.RuleFor(x => x.CstPis)
                .MaximumLength(2).WithMessage("CstPis is too long.")
                .When(x => !string.IsNullOrWhiteSpace(x.CstPis));

            validator.RuleFor(x => x.CstCofins)
                .MaximumLength(2).WithMessage("CstCofins is too long.")
                .When(x => !string.IsNullOrWhiteSpace(x.CstCofins));

            // Rates are fractions, not percentages: the columns are decimal(5,4), so 7.65% is
            // sent as 0.0765. Anything above 1 is a client that used the percentage form.
            validator.RuleFor(x => x.AliquotaIcms)
                .InclusiveBetween(0m, 1m).WithMessage("AliquotaIcms must be between 0 and 1.")
                .When(x => x.AliquotaIcms.HasValue);

            // Exclusive on both ends, unlike a rate: a reduction of zero is CST 00 and a reduction
            // of one is an exemption, and both are a different CST rather than this field.
            validator.RuleFor(x => x.ReducaoBaseIcms)
                .ExclusiveBetween(0m, 1m).WithMessage("ReducaoBaseIcms must be between 0 and 1, exclusive.")
                .When(x => x.ReducaoBaseIcms.HasValue);

            validator.RuleFor(x => x.AliquotaPis)
                .InclusiveBetween(0m, 1m).WithMessage("AliquotaPis must be between 0 and 1.")
                .When(x => x.AliquotaPis.HasValue);

            validator.RuleFor(x => x.AliquotaCofins)
                .InclusiveBetween(0m, 1m).WithMessage("AliquotaCofins must be between 0 and 1.")
                .When(x => x.AliquotaCofins.HasValue);

            validator.RuleFor(x => x.CodigoBeneficioFiscal)
                .MaximumLength(10).WithMessage("CodigoBeneficioFiscal is too long.")
                .When(x => !string.IsNullOrWhiteSpace(x.CodigoBeneficioFiscal));
        }

        private static bool HasDigitCount(string? value, int expected) =>
            DocumentValidationHelper.NormalizeDocument(value).Length == expected;
    }
}
