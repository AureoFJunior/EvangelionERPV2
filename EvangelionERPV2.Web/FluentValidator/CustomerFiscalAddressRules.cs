using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    /// <summary>
    /// The structured destinatário address rules, applied identically to the create and update
    /// customer bodies.
    /// </summary>
    /// <remarks>
    /// Shape only, and every rule is conditional on the field being present. A customer who never
    /// receives an NFe needs none of these, so requiring them here would break ordinary customer
    /// registration. Completeness is checked at issuance instead, where the error can name the
    /// missing field and the note is refused before a number is consumed.
    /// </remarks>
    internal static class CustomerFiscalAddressRules
    {
        // Every Brazilian UF, deliberately NOT the SP/RS/SC/PR allowlist that gates the *emitente*.
        // That allowlist is about which autorizador this system can talk to; the destinatário may
        // live anywhere, and an interstate sale is ordinary NFe — the builder already emits
        // idDest = OperacaoInterestadual and the interstate CFOP for exactly that case.
        private static readonly string[] BrazilianUfs =
        [
            "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG",
            "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"
        ];

        public static void AddTo<TRequest>(AbstractValidator<TRequest> validator)
            where TRequest : ICustomerFiscalAddressFields
        {
            validator.RuleFor(x => x.Logradouro)
                .MaximumLength(60).WithMessage("Logradouro is too long.")
                .When(x => !string.IsNullOrWhiteSpace(x.Logradouro));

            validator.RuleFor(x => x.Numero)
                .MaximumLength(60).WithMessage("Numero is too long.")
                .When(x => !string.IsNullOrWhiteSpace(x.Numero));

            validator.RuleFor(x => x.Complemento)
                .MaximumLength(60).WithMessage("Complemento is too long.")
                .When(x => !string.IsNullOrWhiteSpace(x.Complemento));

            validator.RuleFor(x => x.Bairro)
                .MaximumLength(60).WithMessage("Bairro is too long.")
                .When(x => !string.IsNullOrWhiteSpace(x.Bairro));

            validator.RuleFor(x => x.CodigoMunicipioIbge)
                .Must(value => HasDigitCount(value, 7))
                .WithMessage("CodigoMunicipioIbge must have 7 digits.")
                .When(x => !string.IsNullOrWhiteSpace(x.CodigoMunicipioIbge));

            validator.RuleFor(x => x.Municipio)
                .MaximumLength(60).WithMessage("Municipio is too long.")
                .When(x => !string.IsNullOrWhiteSpace(x.Municipio));

            validator.RuleFor(x => x.Uf)
                .Must(uf => BrazilianUfs.Contains(uf!.Trim().ToUpperInvariant()))
                .WithMessage("Uf must be a Brazilian state code.")
                .When(x => !string.IsNullOrWhiteSpace(x.Uf));

            validator.RuleFor(x => x.Cep)
                .Must(value => HasDigitCount(value, 8))
                .WithMessage("Cep must have 8 digits.")
                .When(x => !string.IsNullOrWhiteSpace(x.Cep));

            validator.RuleFor(x => x.InscricaoEstadual)
                .MaximumLength(14).WithMessage("InscricaoEstadual is too long.")
                .When(x => !string.IsNullOrWhiteSpace(x.InscricaoEstadual));
        }

        private static bool HasDigitCount(string? value, int expected) =>
            DocumentValidationHelper.NormalizeDocument(value).Length == expected;
    }
}
