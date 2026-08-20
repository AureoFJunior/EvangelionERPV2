using EvangelionERPV2.EmployeeModule.Application.Services;
using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    /// <summary>
    /// Shape only. The art. 134 §1 split rules need the accrual's other booked periods to decide,
    /// so they are enforced in <c>EmployeeVacationService</c> where that is loadable — this catches
    /// the payloads that are wrong on their own terms.
    /// </summary>
    public sealed class ScheduleVacationRequestValidator : AbstractValidator<ScheduleVacationRequestDTO>
    {
        public ScheduleVacationRequestValidator()
        {
            RuleFor(x => x.AccrualId)
                .NotEmpty().WithMessage("A férias schedule must reference a período aquisitivo.");

            RuleFor(x => x.StartDate)
                .NotEmpty().WithMessage("Férias start date is required.");

            RuleFor(x => x.Days)
                .GreaterThanOrEqualTo(VacationEntitlement.MinPeriodDays)
                    .WithMessage($"A férias period cannot be shorter than {VacationEntitlement.MinPeriodDays} days (CLT art. 134 §1).")
                // A full entitlement is 30 days; anything longer is a typo, not a schedule.
                .LessThanOrEqualTo(MaxVacationDays)
                    .WithMessage($"A férias period cannot exceed {MaxVacationDays} days.");
        }

        private const int MaxVacationDays = 30;
    }
}
