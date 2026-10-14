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

            RuleFor(x => x.TrackingCode)
                .MaximumLength(40).WithMessage("Tracking code must be 40 characters or fewer.");

            RuleFor(x => x.TrackingUrl)
                .MaximumLength(500).WithMessage("Tracking link must be 500 characters or fewer.")
                .Must(BeAWebAddress).WithMessage("Tracking link must be an http or https address.")
                .When(x => !string.IsNullOrWhiteSpace(x.TrackingUrl));

            RuleFor(x => x.PaymentScheduledDate)
                .Must(x => !x.HasValue || x.Value != default)
                .WithMessage("PaymentScheduledDate is invalid.");
        }

        private static bool BeAWebAddress(string? url) =>
            Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
