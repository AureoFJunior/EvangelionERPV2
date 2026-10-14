using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class UploadFamilyPictureRequestValidator : AbstractValidator<UploadFamilyPictureRequestDTO>
    {
        private const int MaxImageSizeInBytes = 5 * 1024 * 1024;

        public UploadFamilyPictureRequestValidator()
        {
            RuleFor(x => x.VariantGroupId)
                .NotEmpty().WithMessage("VariantGroupId is required.");

            RuleFor(x => x.File)
                .NotEmpty().WithMessage("File is required.")
                .Must(Base64PayloadValidationHelper.IsValidBase64Payload)
                    .WithMessage("File must be a valid base64 payload.")
                .Must(file => Base64PayloadValidationHelper.IsWithinDecodedSizeLimit(file, MaxImageSizeInBytes))
                    .WithMessage("File must be 5 MB or smaller.")
                .Must(Base64PayloadValidationHelper.HasSupportedImageSignature)
                    .WithMessage("File must be PNG, JPEG, GIF, or WEBP.");
        }
    }
}
