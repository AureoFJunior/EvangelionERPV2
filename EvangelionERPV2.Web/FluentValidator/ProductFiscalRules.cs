using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    /// <summary>
    /// The per-product fiscal field rules, applied identically to the create and update bodies.
    /// </summary>
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

            validator.RuleFor(x => x.AliquotaIcms)
                .InclusiveBetween(0m, 1m).WithMessage("AliquotaIcms must be between 0 and 1.")
                .When(x => x.AliquotaIcms.HasValue);

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
