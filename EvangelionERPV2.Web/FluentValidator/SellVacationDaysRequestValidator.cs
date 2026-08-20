using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    /// <summary>
    /// Shape only. The art. 143 third-of-the-entitlement cap depends on the accrual, so it is
    /// enforced in <c>EmployeeVacationService</c>; ten days is the ceiling that holds regardless,
    /// since a third of the largest possible entitlement is ten.
    /// </summary>
    public sealed class SellVacationDaysRequestValidator : AbstractValidator<SellVacationDaysRequestDTO>
    {
        public SellVacationDaysRequestValidator()
        {
            RuleFor(x => x.AccrualId)
                .NotEmpty().WithMessage("An abono pecuniário must reference a período aquisitivo.");

            RuleFor(x => x.Days)
                .GreaterThan(0).WithMessage("An abono pecuniário must be at least one day.")
                .LessThanOrEqualTo(MaxAbonoDays)
                    .WithMessage($"An abono pecuniário cannot exceed {MaxAbonoDays} days (CLT art. 143).");
        }

        private const int MaxAbonoDays = 10;
    }
}
