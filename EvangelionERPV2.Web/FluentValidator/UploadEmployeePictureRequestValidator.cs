using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class UploadEmployeePictureRequestValidator : AbstractValidator<UploadEmployeePictureRequestDTO>
    {
        private const int MaxImageSizeInBytes = 5 * 1024 * 1024;

        public UploadEmployeePictureRequestValidator()
        {
            RuleFor(x => x.EmployeeId)
                .NotEmpty().WithMessage("EmployeeId is required.");

            // File is optional: an empty payload clears the stored picture.
            RuleFor(x => x.File)
                .Must(Base64PayloadValidationHelper.IsValidBase64Payload)
                    .WithMessage("File must be a valid base64 payload.")
                .Must(file => Base64PayloadValidationHelper.IsWithinDecodedSizeLimit(file, MaxImageSizeInBytes))
                    .WithMessage("File must be 5 MB or smaller.")
                .Must(Base64PayloadValidationHelper.HasSupportedImageSignature)
                    .WithMessage("File must be PNG, JPEG, GIF, or WEBP.")
                .When(x => !string.IsNullOrWhiteSpace(x.File));
        }
    }
}
