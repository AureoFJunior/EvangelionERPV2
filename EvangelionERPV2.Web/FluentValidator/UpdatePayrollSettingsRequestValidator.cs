using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class UpdatePayrollSettingsRequestValidator : AbstractValidator<EnterprisePayrollSettingsDTO>
    {
        public UpdatePayrollSettingsRequestValidator()
        {
            RuleFor(x => x.InssRate)
                .InclusiveBetween(0m, 1m).WithMessage("InssRate must be between 0 and 1.");

            RuleFor(x => x.RatRate)
                .InclusiveBetween(0m, 1m).WithMessage("RatRate must be between 0 and 1.");

            RuleFor(x => x.ThirdPartyRate)
                .InclusiveBetween(0m, 1m).WithMessage("ThirdPartyRate must be between 0 and 1.");

            RuleFor(x => x.FgtsRate)
                .InclusiveBetween(0m, 1m).WithMessage("FgtsRate must be between 0 and 1.");

            RuleFor(x => x.PayrollDueDay)
                .InclusiveBetween(1, 28).WithMessage("PayrollDueDay must be between 1 and 28.");
        }
    }
}
