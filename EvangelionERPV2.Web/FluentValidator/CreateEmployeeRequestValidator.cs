using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class CreateEmployeeRequestValidator : AbstractValidator<CreateEmployeeRequestDTO>
    {
        public CreateEmployeeRequestValidator()
        {
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

            RuleFor(x => x.HireDate)
                .LessThanOrEqualTo(_ => DateTime.UtcNow.AddDays(1)).WithMessage("Employee hire date cannot be in the future.")
                .When(x => x.HireDate.HasValue);

            RuleFor(x => x.Picture)
                .Must(Base64PayloadValidationHelper.IsValidBase64Payload)
                    .WithMessage("Employee picture must be a valid base64 payload.")
                .Must(picture => Base64PayloadValidationHelper.IsWithinDecodedSizeLimit(picture, MaxImageSizeInBytes))
                    .WithMessage("Employee picture must be 5 MB or smaller.")
                .Must(Base64PayloadValidationHelper.HasSupportedImageSignature)
                    .WithMessage("Employee picture must be PNG, JPEG, GIF, or WEBP.")
                .When(x => !string.IsNullOrWhiteSpace(x.Picture));
        }

        private const int MaxImageSizeInBytes = 5 * 1024 * 1024;
    }
}
