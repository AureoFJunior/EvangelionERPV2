using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequestDTO>
    {
        public CreateOrderRequestValidator()
        {
            RuleFor(x => x.CustomerId)
                .NotEmpty().WithMessage("CustomerId is required.");

            RuleFor(x => x.PaymentScheduledDate)
                .Must(x => x != default)
                .WithMessage("PaymentScheduledDate is required.");

            RuleFor(x => x.Status)
                .Must(x => Enum.IsDefined(typeof(EnumOrderStatus), x))
                .WithMessage("Invalid order status.")
                .Must(x => x != (int)EnumOrderStatus.Refund)
                .WithMessage("Use refund action to set order as Refund.");

            RuleFor(x => x.Items)
                .NotNull().WithMessage("Items are required.")
                .Must(x => x.Any()).WithMessage("Order must have at least one item.");

            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(x => x.ProductId)
                    .NotEmpty().WithMessage("ProductId is required.");

                item.RuleFor(x => x.Quantity)
                    .GreaterThan(0).WithMessage("Quantity must be greater than zero.");

                item.RuleFor(x => x.Value)
                    .GreaterThan(0).WithMessage("Value must be greater than zero.");
            });
        }
    }
}
