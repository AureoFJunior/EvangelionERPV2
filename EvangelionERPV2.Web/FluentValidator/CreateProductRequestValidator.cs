using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequestDTO>
    {
        private const int MaxImageSizeInBytes = 5 * 1024 * 1024;

        public CreateProductRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Product name is required.")
                .MaximumLength(150).WithMessage("Product name is too long.");

            RuleFor(x => x.DefaultValue)
                .GreaterThanOrEqualTo(0).WithMessage("DefaultValue must be zero or greater.");

            RuleFor(x => x.StorageQuantity)
                .GreaterThanOrEqualTo(0).WithMessage("StorageQuantity must be zero or greater.");

            RuleFor(x => x.UnitOfMeasure)
                .NotEmpty().WithMessage("UnitOfMeasure is required.")
                .MaximumLength(30).WithMessage("UnitOfMeasure is too long.");

            RuleFor(x => x.File)
                .NotEmpty().WithMessage("Product image file is required.")
                .Must(Base64PayloadValidationHelper.IsValidBase64Payload)
                    .WithMessage("Product image file must be a valid base64 payload.")
                .Must(file => Base64PayloadValidationHelper.IsWithinDecodedSizeLimit(file, MaxImageSizeInBytes))
                    .WithMessage("Product image file must be 5 MB or smaller.")
                .Must(Base64PayloadValidationHelper.HasSupportedImageSignature)
                    .WithMessage("Product image file must be PNG, JPEG, GIF, or WEBP.");
        }
    }
}
