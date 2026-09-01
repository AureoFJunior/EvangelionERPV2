using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    /// <summary>
    /// Front door validation. NFeFiscalSettingsService re-validates everything server-side —
    /// this exists to give the client field-level errors, not to be the only guard.
    /// </summary>
    public sealed class UpdateFiscalSettingsRequestValidator : AbstractValidator<EnterpriseFiscalSettingsDTO>
    {
        private static readonly string[] SupportedUfs = ["SP", "RS", "SC", "PR"];

        public UpdateFiscalSettingsRequestValidator()
        {
            RuleFor(x => x.Cnpj)
                .Must(DocumentValidationHelper.BeValidCnpj)
                .WithMessage("Cnpj must be a valid CNPJ.");

            RuleFor(x => x.InscricaoEstadual)
                .NotEmpty().WithMessage("InscricaoEstadual is required.")
                .MaximumLength(14);

            RuleFor(x => x.RazaoSocial)
                .NotEmpty().WithMessage("RazaoSocial is required.")
                .MaximumLength(60);

            RuleFor(x => x.NomeFantasia).MaximumLength(60);

            RuleFor(x => x.Crt)
                .InclusiveBetween((byte)1, (byte)4).WithMessage("Crt must be between 1 and 4.");

            RuleFor(x => x.Uf)
                .Must(uf => !string.IsNullOrWhiteSpace(uf)
                            && SupportedUfs.Contains(uf.Trim().ToUpperInvariant()))
                .WithMessage("Uf must be one of SP, RS, SC or PR.");

            RuleFor(x => x.Logradouro).NotEmpty().MaximumLength(60);
            RuleFor(x => x.Numero).NotEmpty().MaximumLength(60);
            RuleFor(x => x.Complemento).MaximumLength(60);
            RuleFor(x => x.Bairro).NotEmpty().MaximumLength(60);
            RuleFor(x => x.Municipio).NotEmpty().MaximumLength(60);

            RuleFor(x => x.CodigoMunicipioIbge)
                .Must(value => HasDigitCount(value, 7))
                .WithMessage("CodigoMunicipioIbge must have 7 digits.");

            RuleFor(x => x.Cep)
                .Must(value => HasDigitCount(value, 8))
                .WithMessage("Cep must have 8 digits.");

            RuleFor(x => x.Environment)
                .Must(value => value is 1 or 2)
                .WithMessage("Environment must be 1 (producao) or 2 (homologacao).");

            RuleFor(x => x.NFeSeries).NotEmpty().MaximumLength(3);
            RuleFor(x => x.NFCeSeries).NotEmpty().MaximumLength(3);

            // natOp is 1..60 on the wire. Empty is allowed here and filled with the default on the
            // way in, so a tenant that clears the box gets "VENDA DE MERCADORIA" and never a note
            // SEFAZ refuses for an empty element.
            RuleFor(x => x.DefaultNatOp).MaximumLength(60);

            RuleFor(x => x.DefaultCfopIntra)
                .Must(value => HasDigitCount(value, 4))
                .WithMessage("DefaultCfopIntra must have 4 digits.");

            RuleFor(x => x.DefaultCfopInter)
                .Must(value => HasDigitCount(value, 4))
                .WithMessage("DefaultCfopInter must have 4 digits.");

            RuleFor(x => x.DefaultCst)
                .MaximumLength(3)
                .NotEmpty().When(x => x.Crt == 3)
                .WithMessage("DefaultCst is required for Regime Normal (Crt 3).");

            RuleFor(x => x.DefaultCsosn)
                .MaximumLength(3)
                .NotEmpty().When(x => x.Crt is 1 or 2)
                .WithMessage("DefaultCsosn is required for Simples Nacional (Crt 1 or 2).");

            RuleFor(x => x.DefaultOrigem)
                .InclusiveBetween((byte)0, (byte)8).WithMessage("DefaultOrigem must be between 0 and 8.");

            RuleFor(x => x.DefaultAliquotaIcms).InclusiveBetween(0m, 1m);
            RuleFor(x => x.AliquotaCreditoSn).ExclusiveBetween(0m, 1m).When(x => x.AliquotaCreditoSn.HasValue);
            RuleFor(x => x.DefaultAliquotaPis).InclusiveBetween(0m, 1m);
            RuleFor(x => x.DefaultAliquotaCofins).InclusiveBetween(0m, 1m);

            RuleFor(x => x.NFeCancellationWindowMinutes)
                .InclusiveBetween(1, 1440)
                .WithMessage("NFeCancellationWindowMinutes must be between 1 and 1440.");

            RuleFor(x => x.NFCeCancellationWindowMinutes)
                .InclusiveBetween(1, 1440)
                .WithMessage("NFCeCancellationWindowMinutes must be between 1 and 1440.");

            RuleFor(x => x.NFCeCustomerIdentificationThreshold)
                .GreaterThanOrEqualTo(0m);
        }

        private static bool HasDigitCount(string? value, int expected) =>
            DocumentValidationHelper.NormalizeDocument(value).Length == expected;
    }
}
