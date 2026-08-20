using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class ResolveBankStatementLineRequestValidator : AbstractValidator<ResolveBankStatementLineRequestDTO>
    {
        public ResolveBankStatementLineRequestValidator()
        {
            RuleFor(x => x.LineId)
                .NotEmpty().WithMessage("Statement line is required.");

            // Non-empty only. Which side of the statement the document has to sit on, and whether
            // it is already reconciled elsewhere, are database questions the service answers.
            RuleFor(x => x.MatchedReferenceId)
                .NotEmpty().WithMessage("A document to match is required.");
        }
    }

    public sealed class IgnoreBankStatementLineRequestValidator : AbstractValidator<IgnoreBankStatementLineRequestDTO>
    {
        public IgnoreBankStatementLineRequestValidator()
        {
            RuleFor(x => x.LineId)
                .NotEmpty().WithMessage("Statement line is required.");
        }
    }
}
