using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class NFeCancelRequestValidator : AbstractValidator<NFeCancelRequestDTO>
    {
        public NFeCancelRequestValidator()
        {
            // SEFAZ requires 15 to 255 characters for xJust on event 110111. Enforced here so a
            // rejection that costs a round trip becomes a 400 instead.
            RuleFor(x => x.Reason)
                .NotEmpty().WithMessage("Cancellation reason is required.")
                .Length(15, 255).WithMessage("Cancellation reason must be between 15 and 255 characters.");
        }
    }

    public sealed class UploadNFeCertificateRequestValidator : AbstractValidator<UploadNFeCertificateRequestDTO>
    {
        private const long MaxCertificateSizeInBytes = 512 * 1024;

        public UploadNFeCertificateRequestValidator()
        {
            RuleFor(x => x.File)
                .NotNull().WithMessage("Certificate file is required.")
                .Must(file => file!.Length > 0).WithMessage("Certificate file is empty.")
                .Must(file => file!.Length <= MaxCertificateSizeInBytes)
                    .WithMessage("Certificate file must be 512 KB or smaller.");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Certificate password is required.")
                .MaximumLength(256).WithMessage("Certificate password is too long.");
        }
    }

    public sealed class SettleBillRequestValidator : AbstractValidator<SettleBillRequestDTO>
    {
        // decimal(18,2) end to end, so 16 integer digits is the widest value the column holds.
        private const decimal MaxPaidAmount = 9_999_999_999_999_999.99m;

        public SettleBillRequestValidator()
        {
            RuleFor(x => x.PaidAmount)
                .GreaterThan(0).WithMessage("Paid amount must be greater than zero.")
                .LessThanOrEqualTo(MaxPaidAmount).WithMessage("Paid amount is too large.");

            // A settlement dated in the future would be projected by the cash-flow forecast as
            // money that has not arrived. The service re-checks this; the 400 is cheaper.
            RuleFor(x => x.PaidAt!.Value)
                .LessThanOrEqualTo(_ => DateTime.UtcNow.Date.AddDays(1).AddTicks(-1))
                    .WithMessage("Settlement date cannot be in the future.")
                .When(x => x.PaidAt.HasValue);

            RuleFor(x => x.SettlementProtocol!)
                .MaximumLength(64).WithMessage("Settlement protocol is too long.")
                .When(x => x.SettlementProtocol != null);
        }
    }

    public sealed class RefundRequestValidator : AbstractValidator<RefundRequestDTO>
    {
        public RefundRequestValidator()
        {
            RuleFor(x => x.Reason)
                .NotEmpty().WithMessage("Refund reason is required.")
                .MaximumLength(500).WithMessage("Refund reason is too long.");
        }
    }

    public sealed class CreateReportRequestValidator : AbstractValidator<CreateReportRequestDTO>
    {
        public CreateReportRequestValidator()
        {
            RuleFor(x => x.Type)
                .Must(type => Enum.IsDefined(typeof(EnumReportType), type))
                .WithMessage("Report type is not supported.");
        }
    }
}
