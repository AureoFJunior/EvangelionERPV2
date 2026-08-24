using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequestDTO>
    {
        public UpdateCustomerRequestValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty().WithMessage("Customer id is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Customer name is required.")
                .MaximumLength(150).WithMessage("Customer name is too long.");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Customer email is required.")
                .EmailAddress().WithMessage("Customer email is invalid.");

            RuleFor(x => x.PhoneNumber)
                .NotEmpty().WithMessage("Customer phone is required.")
                .MaximumLength(30).WithMessage("Customer phone is too long.");

            RuleFor(x => x.Adress)
                .NotEmpty().WithMessage("Customer address is required.")
                .MaximumLength(250).WithMessage("Customer address is too long.");

            // Zero is legitimate — cash only — so the floor is zero, not one.
            RuleFor(x => x.CreditLimit)
                .GreaterThanOrEqualTo(0).WithMessage("Credit limit cannot be negative.")
                .When(x => x.CreditLimit.HasValue);

            RuleFor(x => x.Document)
                .Must(DocumentValidationHelper.BeValidDocument)
                .When(x => !string.IsNullOrWhiteSpace(x.Document))
                .WithMessage("Customer document must be a valid CPF, CNPJ, or NIF.");

            CustomerFiscalAddressRules.AddTo(this);
        }
    }
}
