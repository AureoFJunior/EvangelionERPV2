using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequestDTO>
    {
        private const int MaxProfilePictureSizeInBytes = 5 * 1024 * 1024;

        public CreateUserRequestValidator()
        {
            RuleFor(x => x.FirstName)
                .NotEmpty().WithMessage("First name is required.")
                .MaximumLength(100).WithMessage("First name is too long.");

            RuleFor(x => x.LastName)
                .NotEmpty().WithMessage("Last name is required.")
                .MaximumLength(100).WithMessage("Last name is too long.");

            RuleFor(x => x.UserName)
                .NotEmpty().WithMessage("User name is required.")
                .MaximumLength(100).WithMessage("User name is too long.");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("Email is invalid.");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required.")
                .MinimumLength(8).WithMessage("Password must have at least 8 characters.")
                .MaximumLength(128).WithMessage("Password is too long.");

            RuleFor(x => x.BirthDate)
                .LessThan(_ => DateTime.UtcNow).WithMessage("Birth date must be in the past.");

            RuleFor(x => x.Language)
                .Must(language => Enum.IsDefined(typeof(EnumLanguage), (int)language))
                .WithMessage("Language is not supported.");

            RuleFor(x => x.ProfilePicture)
                .Must(Base64PayloadValidationHelper.IsValidBase64Payload)
                    .WithMessage("Profile picture must be a valid base64 payload.")
                .Must(picture => Base64PayloadValidationHelper.IsWithinDecodedSizeLimit(picture, MaxProfilePictureSizeInBytes))
                    .WithMessage("Profile picture must be 5 MB or smaller.")
                .Must(Base64PayloadValidationHelper.HasSupportedImageSignature)
                    .WithMessage("Profile picture must be PNG, JPEG, GIF, or WEBP.")
                .When(x => !string.IsNullOrWhiteSpace(x.ProfilePicture));
        }
    }

    public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequestDTO>
    {
        private const int MaxProfilePictureSizeInBytes = 5 * 1024 * 1024;

        public UpdateUserRequestValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty().WithMessage("User id is required.");

            RuleFor(x => x.FirstName)
                .NotEmpty().WithMessage("First name is required.")
                .MaximumLength(100).WithMessage("First name is too long.");

            RuleFor(x => x.LastName)
                .NotEmpty().WithMessage("Last name is required.")
                .MaximumLength(100).WithMessage("Last name is too long.");

            RuleFor(x => x.UserName)
                .NotEmpty().WithMessage("User name is required.")
                .MaximumLength(100).WithMessage("User name is too long.");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("Email is invalid.");

            // Blank means "keep the stored password", so length only applies to a real change.
            RuleFor(x => x.Password)
                .MinimumLength(8).WithMessage("Password must have at least 8 characters.")
                .MaximumLength(128).WithMessage("Password is too long.")
                .When(x => !string.IsNullOrWhiteSpace(x.Password));

            RuleFor(x => x.BirthDate)
                .LessThan(_ => DateTime.UtcNow).WithMessage("Birth date must be in the past.");

            RuleFor(x => x.AccessLevel)
                .Must(level => Enum.IsDefined(typeof(EnumAccessLevel), (int)level))
                .WithMessage("Invalid access level.");

            RuleFor(x => x.Language)
                .Must(language => Enum.IsDefined(typeof(EnumLanguage), (int)language))
                .WithMessage("Language is not supported.");

            // A fraction, not a percentage: 0.05 is 5%. Capped at 1 because a rate above 100% pays
            // the seller more than the sale brought in, which is a typo every time.
            RuleFor(x => x.CommissionRate)
                .InclusiveBetween(0m, 1m).WithMessage("Commission rate must be between 0 and 1.")
                .When(x => x.CommissionRate.HasValue);

            RuleFor(x => x.ProfilePicture)
                .Must(Base64PayloadValidationHelper.IsValidBase64Payload)
                    .WithMessage("Profile picture must be a valid base64 payload.")
                .Must(picture => Base64PayloadValidationHelper.IsWithinDecodedSizeLimit(picture, MaxProfilePictureSizeInBytes))
                    .WithMessage("Profile picture must be 5 MB or smaller.")
                .Must(Base64PayloadValidationHelper.HasSupportedImageSignature)
                    .WithMessage("Profile picture must be PNG, JPEG, GIF, or WEBP.")
                .When(x => !string.IsNullOrWhiteSpace(x.ProfilePicture));
        }
    }
}
