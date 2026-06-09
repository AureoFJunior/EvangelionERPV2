using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequestDTO>
    {
        private const int MaxImageSizeInBytes = 5 * 1024 * 1024;

        public CreateProductRequestValidator()
        {
            RuleFor(x => ResolveName(x))
                .NotEmpty().WithMessage("Product name is required.")
                .MaximumLength(150).WithMessage("Product name is too long.");

            RuleFor(x => ResolveDefaultValue(x))
                .GreaterThanOrEqualTo(0).WithMessage("DefaultValue must be zero or greater.");

            RuleFor(x => ResolveStorageQuantity(x))
                .GreaterThanOrEqualTo(0).WithMessage("StorageQuantity must be zero or greater.");

            RuleFor(x => ResolveUnitOfMeasure(x))
                .NotEmpty().WithMessage("UnitOfMeasure is required.")
                .MaximumLength(30).WithMessage("UnitOfMeasure is too long.");

            RuleFor(x => x.File)
                .Must(Base64PayloadValidationHelper.IsValidBase64Payload)
                    .WithMessage("Product image file must be a valid base64 payload.")
                .Must(file => Base64PayloadValidationHelper.IsWithinDecodedSizeLimit(file, MaxImageSizeInBytes))
                    .WithMessage("Product image file must be 5 MB or smaller.")
                .Must(Base64PayloadValidationHelper.HasSupportedImageSignature)
                    .WithMessage("Product image file must be PNG, JPEG, GIF, or WEBP.")
                .When(x => !string.IsNullOrWhiteSpace(x.File));
        }

        private static string ResolveName(CreateProductRequestDTO request)
        {
            if (!string.IsNullOrWhiteSpace(request.Name))
                return request.Name;

            return request.Product?.Name ?? string.Empty;
        }

        private static double ResolveDefaultValue(CreateProductRequestDTO request)
        {
            return request.DefaultValue ?? request.Product?.DefaultValue ?? 0;
        }

        private static double ResolveStorageQuantity(CreateProductRequestDTO request)
        {
            return request.StorageQuantity ?? request.Product?.StorageQuantity ?? 0;
        }

        private static string ResolveUnitOfMeasure(CreateProductRequestDTO request)
        {
            if (!string.IsNullOrWhiteSpace(request.UnitOfMeasure))
                return request.UnitOfMeasure;

            return request.Product?.UnitOfMeasure ?? string.Empty;
        }
    }
}
