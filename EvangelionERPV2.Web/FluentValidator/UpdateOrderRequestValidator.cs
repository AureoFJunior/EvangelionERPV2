using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class UpdateOrderRequestValidator : AbstractValidator<UpdateOrderRequestDTO>
    {
        public UpdateOrderRequestValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty().WithMessage("Order id is required.");

            RuleFor(x => x.Status)
                .Must(x => Enum.IsDefined(typeof(EnumOrderStatus), x))
                .WithMessage("Invalid order status.")
                .Must(x => x != (int)EnumOrderStatus.Refund)
                .WithMessage("Use refund action to set order as Refund.");

            RuleFor(x => x.PaymentScheduledDate)
                .Must(x => !x.HasValue || x.Value != default)
                .WithMessage("PaymentScheduledDate is invalid.");
        }
    }
}
