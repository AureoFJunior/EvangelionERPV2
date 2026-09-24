using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class UpsertPayableBillRequestValidator : AbstractValidator<UpsertPayableBillRequestDTO>
    {
        public UpsertPayableBillRequestValidator()
        {
            RuleFor(x => x.Description)
                .NotEmpty().WithMessage("Description is required.")
                .MaximumLength(300).WithMessage("Description is too long.");

            RuleFor(x => x.BillType)
                .Must(x => Enum.IsDefined(typeof(EnumPayableBillType), x))
                .WithMessage("Invalid payable bill type.");

            RuleFor(x => x.DueDate)
                .Must(x => x != default)
                .WithMessage("DueDate is required.");

            RuleFor(x => x.RecurrenceMonths)
                .InclusiveBetween(1, 12).WithMessage("Recurrence must be between 1 and 12 months.")
                .When(x => x.RecurrenceMonths.HasValue);

            // Cascade(Stop) is load-bearing, not style: FluentValidation runs every component of a
            // rule by default, so without it the date comparison below dereferenced the null that
            // NotNull had just reported and the request answered 500 instead of 400.
            RuleFor(x => x.RecurrenceUntilUtc)
                .Cascade(CascadeMode.Stop)
                .NotNull().WithMessage("A recurring bill needs an end date.")
                .Must((request, until) => until!.Value.Date > request.DueDate.Date)
                    .WithMessage("The recurrence end date must be after the first due date.")
                .When(x => x.RecurrenceMonths.HasValue);

            RuleFor(x => x.Amount)
                .Must((request, amount) =>
                {
                    if (request.Items != null && request.Items.Any())
                        return true;

                    return amount.HasValue && amount.Value > 0;
                })
                .WithMessage("Amount must be greater than zero when no items are provided.");

            RuleFor(x => x.Items)
                .Must(items => items == null || items.Select(i => i.ProductId).Distinct().Count() == items.Count())
                .WithMessage("Payable bill has duplicate products.");

            RuleForEach(x => x.Items!).ChildRules(item =>
            {
                item.RuleFor(x => x.ProductId)
                    .NotEmpty().WithMessage("ProductId is required.");

                item.RuleFor(x => x.Quantity)
                    .GreaterThan(0).WithMessage("Quantity must be greater than zero.");

                item.RuleFor(x => x.UnitValue)
                    .GreaterThanOrEqualTo(0).WithMessage("UnitValue must be zero or greater.");
            });
        }
    }
}
