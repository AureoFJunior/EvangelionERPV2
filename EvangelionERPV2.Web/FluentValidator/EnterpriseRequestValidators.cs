using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class CreateEnterpriseRequestValidator : AbstractValidator<CreateEnterpriseRequestDTO>
    {
        public CreateEnterpriseRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Enterprise name is required.")
                .MaximumLength(150).WithMessage("Enterprise name is too long.");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Enterprise email is required.")
                .EmailAddress().WithMessage("Enterprise email is invalid.");

            RuleFor(x => x.PhoneNumber)
                .NotEmpty().WithMessage("Enterprise phone is required.")
                .MaximumLength(30).WithMessage("Enterprise phone is too long.");

            RuleFor(x => x.Adress)
                .NotEmpty().WithMessage("Enterprise address is required.")
                .MaximumLength(250).WithMessage("Enterprise address is too long.");

            RuleFor(x => x.Currency)
                .Length(3).WithMessage("Currency must be a three-letter ISO 4217 code.")
                .When(x => !string.IsNullOrWhiteSpace(x.Currency));
        }
    }

    public sealed class ClosePeriodRequestValidator : AbstractValidator<ClosePeriodRequestDTO>
    {
        public ClosePeriodRequestValidator()
        {
            // A lock in the future would close months nobody has lived through yet, and every
            // write into them would answer 409 with no way to explain why.
            RuleFor(x => x.ClosedThroughUtc)
                .Must(value => value!.Value.Date <= DateTime.UtcNow.Date)
                .WithMessage("The closing date cannot be in the future.")
                .When(x => x.ClosedThroughUtc.HasValue);
        }
    }

    public sealed class UpdateEnterpriseRequestValidator : AbstractValidator<UpdateEnterpriseRequestDTO>
    {
        public UpdateEnterpriseRequestValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty().WithMessage("Enterprise id is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Enterprise name is required.")
                .MaximumLength(150).WithMessage("Enterprise name is too long.");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Enterprise email is required.")
                .EmailAddress().WithMessage("Enterprise email is invalid.");

            RuleFor(x => x.PhoneNumber)
                .NotEmpty().WithMessage("Enterprise phone is required.")
                .MaximumLength(30).WithMessage("Enterprise phone is too long.");

            RuleFor(x => x.Adress)
                .NotEmpty().WithMessage("Enterprise address is required.")
                .MaximumLength(250).WithMessage("Enterprise address is too long.");

            RuleFor(x => x.Currency)
                .Length(3).WithMessage("Currency must be a three-letter ISO 4217 code.")
                .When(x => !string.IsNullOrWhiteSpace(x.Currency));
        }
    }
}
