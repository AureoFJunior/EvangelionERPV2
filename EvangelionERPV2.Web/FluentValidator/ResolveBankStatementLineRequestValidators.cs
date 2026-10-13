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
