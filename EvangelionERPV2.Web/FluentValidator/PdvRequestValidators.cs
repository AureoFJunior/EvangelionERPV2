using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class OpenCashSessionRequestValidator : AbstractValidator<OpenCashSessionRequestDTO>
    {
        public OpenCashSessionRequestValidator()
        {
            RuleFor(x => x.ClientSessionId)
                .NotEmpty().WithMessage("ClientSessionId is required.");

            RuleFor(x => x.TerminalId)
                .NotEmpty().WithMessage("TerminalId is required.")
                .MaximumLength(64).WithMessage("TerminalId is too long.");

            RuleFor(x => x.OpeningFloat)
                .GreaterThanOrEqualTo(0).WithMessage("OpeningFloat must be zero or greater.");
        }
    }

    public sealed class CloseCashSessionRequestValidator : AbstractValidator<CloseCashSessionRequestDTO>
    {
        public CloseCashSessionRequestValidator()
        {
            RuleFor(x => x.ClientSessionId)
                .NotEmpty().WithMessage("ClientSessionId is required.");

            RuleFor(x => x.CountedAmount)
                .GreaterThanOrEqualTo(0).WithMessage("CountedAmount must be zero or greater.");
        }
    }

    public sealed class CashMovementRequestValidator : AbstractValidator<CashMovementRequestDTO>
    {
        public CashMovementRequestValidator()
        {
            RuleFor(x => x.ClientMovementId)
                .NotEmpty().WithMessage("ClientMovementId is required.");

            RuleFor(x => x.ClientSessionId)
                .NotEmpty().WithMessage("ClientSessionId is required.");

            // Only the two an operator types. A Sale movement posted here would let somebody
            // inflate the drawer to cover a shortfall.
            RuleFor(x => x.Type)
                .Must(type => type == (int)EnumCashMovementType.Sangria || type == (int)EnumCashMovementType.Suprimento)
                .WithMessage("Only a sangria or a suprimento may be registered.");

            RuleFor(x => x.Amount)
                .GreaterThan(0).WithMessage("Amount must be greater than zero.");

            // Cash leaving a drawer without a stated reason is indistinguishable from theft.
            RuleFor(x => x.Reason)
                .NotEmpty().WithMessage("Reason is required.")
                .MaximumLength(200).WithMessage("Reason is too long.");
        }
    }

    public sealed class PdvSaleSyncRequestValidator : AbstractValidator<PdvSaleSyncRequestDTO>
    {
        /// <summary>
        /// A basket this size is a bug or an attack. A real trolley tops out well under it, and the
        /// sale still has to be rung item by item by a person.
        /// </summary>
        private const int MaxItems = 500;
        private const int MaxPayments = 20;

        public PdvSaleSyncRequestValidator()
        {
            RuleFor(x => x.ClientSaleId)
                .NotEmpty().WithMessage("ClientSaleId is required.");

            RuleFor(x => x.ClientSessionId)
                .NotEmpty().WithMessage("ClientSessionId is required.");

            RuleFor(x => x.TerminalId)
                .MaximumLength(64).WithMessage("TerminalId is too long.");

            RuleFor(x => x.DiscountAmount)
                .GreaterThanOrEqualTo(0).WithMessage("DiscountAmount must be zero or greater.");

            RuleFor(x => x.SurchargeAmount)
                .GreaterThanOrEqualTo(0).WithMessage("SurchargeAmount must be zero or greater.");

            RuleFor(x => x.Items)
                .NotEmpty().WithMessage("A sale must have at least one item.")
                .Must(items => items.Count() <= MaxItems).WithMessage("Too many items on one sale.");

            RuleForEach(x => x.Items).SetValidator(new PdvSaleItemRequestValidator());

            RuleFor(x => x.Payments)
                .NotEmpty().WithMessage("A sale must have at least one payment.")
                .Must(payments => payments.Count() <= MaxPayments).WithMessage("Too many payments on one sale.");

            RuleForEach(x => x.Payments).SetValidator(new PdvSalePaymentRequestValidator());
        }
    }

    public sealed class PdvSaleItemRequestValidator : AbstractValidator<PdvSaleItemRequestDTO>
    {
        public PdvSaleItemRequestValidator()
        {
            RuleFor(x => x.ProductId)
                .NotEmpty().WithMessage("ProductId is required.");

            RuleFor(x => x.Quantity)
                .GreaterThan(0).WithMessage("Quantity must be greater than zero.");

            // Zero is allowed: a giveaway line still has to appear on the receipt and on the note.
            RuleFor(x => x.Value)
                .GreaterThanOrEqualTo(0).WithMessage("Value must be zero or greater.");
        }
    }

    public sealed class PdvSalePaymentRequestValidator : AbstractValidator<PdvSalePaymentRequestDTO>
    {
        public PdvSalePaymentRequestValidator()
        {
            RuleFor(x => x.Method)
                .Must(method => Enum.IsDefined(typeof(EnumPaymentMethod), method))
                .WithMessage("Unknown payment method.");

            RuleFor(x => x.Amount)
                .GreaterThan(0).WithMessage("A payment must be greater than zero.");

            RuleFor(x => x.ChangeGiven)
                .GreaterThanOrEqualTo(0).WithMessage("ChangeGiven must be zero or greater.");

            // Only cash can hand notes back. Change on a card is a refund, not a tender.
            RuleFor(x => x)
                .Must(payment => payment.ChangeGiven == 0 || payment.Method == (int)EnumPaymentMethod.Cash)
                .WithMessage("Only a cash payment can give change.");

            RuleFor(x => x.AuthorizationCode)
                .MaximumLength(64).WithMessage("AuthorizationCode is too long.");

            RuleFor(x => x.Installments)
                .InclusiveBetween(1, 36).WithMessage("Installments must be between 1 and 36.")
                .When(x => x.Installments.HasValue);
        }
    }

    public sealed class PdvSaleSyncBatchRequestValidator : AbstractValidator<PdvSaleSyncBatchRequestDTO>
    {
        public PdvSaleSyncBatchRequestValidator()
        {
            RuleFor(x => x.Sales)
                .NotEmpty().WithMessage("A batch must carry at least one sale.");

            RuleForEach(x => x.Sales).SetValidator(new PdvSaleSyncRequestValidator());
        }
    }

    public sealed class EnrollTerminalRequestValidator : AbstractValidator<EnrollTerminalRequestDTO>
    {
        public EnrollTerminalRequestValidator()
        {
            RuleFor(x => x.TerminalId)
                .NotEmpty().WithMessage("TerminalId is required.")
                .MaximumLength(64).WithMessage("TerminalId is too long.");
        }
    }

    public sealed class PdvLoginRequestValidator : AbstractValidator<PdvLoginRequestDTO>
    {
        public PdvLoginRequestValidator()
        {
            RuleFor(x => x.UserName)
                .NotEmpty().WithMessage("UserName is required.")
                .MaximumLength(256).WithMessage("UserName is too long.");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required.")
                .MaximumLength(256).WithMessage("Password is too long.");
        }
    }

    public sealed class SetPdvPinRequestValidator : AbstractValidator<SetPdvPinRequestDTO>
    {
        public SetPdvPinRequestValidator()
        {
            RuleFor(x => x.Pin)
                .NotEmpty().WithMessage("Pin is required.")
                .Matches("^[0-9]{6,12}$").WithMessage("Pin must be 6 to 12 digits.");
        }
    }
}
