using EvangelionERPV2.Shared.DTOs;
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

            RuleFor(x => x.Document)
                .Must(DocumentValidationHelper.BeValidDocument)
                .When(x => !string.IsNullOrWhiteSpace(x.Document))
                .WithMessage("Customer document must be a valid CPF, CNPJ, or NIF.");
        }
    }
}
