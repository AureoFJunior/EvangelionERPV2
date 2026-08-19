using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class UpdateEmployeeRequestValidator : AbstractValidator<UpdateEmployeeRequestDTO>
    {
        public UpdateEmployeeRequestValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty().WithMessage("Employee id is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Employee name is required.")
                .MaximumLength(150).WithMessage("Employee name is too long.");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Employee email is required.")
                .EmailAddress().WithMessage("Employee email is invalid.");

            RuleFor(x => x.PhoneNumber)
                .NotEmpty().WithMessage("Employee phone is required.")
                .MaximumLength(30).WithMessage("Employee phone is too long.");

            RuleFor(x => x.Position)
                .NotEmpty().WithMessage("Employee position is required.")
                .MaximumLength(100).WithMessage("Employee position is too long.");

            RuleFor(x => x.Document)
                .Must(DocumentValidationHelper.BeValidDocument)
                .When(x => !string.IsNullOrWhiteSpace(x.Document))
                .WithMessage("Employee document must be a valid CPF, CNPJ, or NIF.");

            RuleFor(x => x.Salary)
                .GreaterThanOrEqualTo(0).WithMessage("Employee salary cannot be negative.")
                .When(x => x.Salary.HasValue);

            RuleFor(x => x.TerminationDate)
                .GreaterThanOrEqualTo(x => x.HireDate!.Value).WithMessage("Employee termination date cannot be before the hire date.")
                .When(x => x.TerminationDate.HasValue && x.HireDate.HasValue);
        }
    }
}
