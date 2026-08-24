using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class UpsertQuoteRequestValidator : AbstractValidator<UpsertQuoteRequestDTO>
    {
        private const int MaxItems = 200;

        public UpsertQuoteRequestValidator()
        {
            RuleFor(x => x.Items)
                .NotEmpty().WithMessage("A quote needs at least one item.")
                .Must(items => items == null || items.Count() <= MaxItems)
                    .WithMessage($"A quote cannot carry more than {MaxItems} items.");

            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(x => x.ProductId).NotEmpty().WithMessage("Each quote item needs a product.");
                item.RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Each quote item needs a quantity above zero.");
                item.RuleFor(x => x.UnitValue).GreaterThanOrEqualTo(0).WithMessage("A quoted price cannot be negative.");
            });

            // A validity that has already lapsed produces a quote nobody can convert, which is a
            // support call rather than a document.
            RuleFor(x => x.ValidUntil)
                .Must(value => value!.Value.Date >= DateTime.UtcNow.Date)
                .WithMessage("The validity date cannot be in the past.")
                .When(x => x.ValidUntil.HasValue);

            RuleFor(x => x.Notes)
                .MaximumLength(1000).WithMessage("The note is too long.")
                .When(x => !string.IsNullOrEmpty(x.Notes));
        }
    }

    public sealed class QuoteStatusRequestValidator : AbstractValidator<QuoteStatusRequestDTO>
    {
        public QuoteStatusRequestValidator()
        {
            // Converted is reached by converting, never by asserting it; Draft is where a quote
            // starts and Expired is read off the clock.
            RuleFor(x => x.Status)
                .Must(status => status == (int)EnumQuoteStatus.Sent
                                || status == (int)EnumQuoteStatus.Accepted
                                || status == (int)EnumQuoteStatus.Rejected)
                .WithMessage("A quote can only be marked as sent, accepted or rejected.");
        }
    }

    public sealed class ConvertQuoteRequestValidator : AbstractValidator<ConvertQuoteRequestDTO>
    {
        public ConvertQuoteRequestValidator()
        {
            RuleFor(x => x.PaymentScheduledDate)
                .Must(value => value!.Value.Date >= DateTime.UtcNow.Date.AddYears(-1))
                .WithMessage("The payment date is too far in the past.")
                .When(x => x.PaymentScheduledDate.HasValue);
        }
    }
}
