using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class UpdateSupplierRequestValidator : AbstractValidator<UpdateSupplierRequestDTO>
    {
        public UpdateSupplierRequestValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty().WithMessage("Supplier id is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Supplier name is required.")
                .MaximumLength(150).WithMessage("Supplier name is too long.");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Supplier email is required.")
                .EmailAddress().WithMessage("Supplier email is invalid.");

            RuleFor(x => x.PhoneNumber)
                .NotEmpty().WithMessage("Supplier phone is required.")
                .MaximumLength(30).WithMessage("Supplier phone is too long.");

            RuleFor(x => x.Adress)
                .NotEmpty().WithMessage("Supplier address is required.")
                .MaximumLength(250).WithMessage("Supplier address is too long.");

            RuleFor(x => x.Document)
                .Must(DocumentValidationHelper.BeValidDocument)
                .When(x => !string.IsNullOrWhiteSpace(x.Document))
                .WithMessage("Supplier document must be a valid CPF, CNPJ, or NIF.");
        }
    }
}
