using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class UpsertSalesGoalRequestValidator : AbstractValidator<UpsertSalesGoalRequestDTO>
    {
        // A target no company will ever bill. Bounded so a fat-fingered extra digit is refused at
        // the edge rather than rendering an attainment of 0.0% forever.
        private const decimal MaxTargetAmount = 1_000_000_000m;

        public UpsertSalesGoalRequestValidator()
        {
            RuleFor(x => x.Year)
                .InclusiveBetween(2000, 2100).WithMessage("Year must be between 2000 and 2100.");

            RuleFor(x => x.Month)
                .InclusiveBetween(1, 12).WithMessage("Month must be between 1 and 12.");

            RuleFor(x => x.TargetAmount)
                .GreaterThanOrEqualTo(0).WithMessage("Target amount cannot be negative.")
                .LessThanOrEqualTo(MaxTargetAmount).WithMessage("Target amount is too large.");
        }
    }
}
