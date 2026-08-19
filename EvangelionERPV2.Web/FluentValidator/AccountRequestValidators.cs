using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Web.Controllers;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    /// <summary>
    /// The authentication and self-service bodies. They were unvalidated until now, so an empty or
    /// oversized field reached the service layer and failed there, if at all.
    /// </summary>
    public sealed class LoginRequestValidator : AbstractValidator<LoginRequestDTO>
    {
        public LoginRequestValidator()
        {
            RuleFor(x => x.UserName)
                .NotEmpty().WithMessage("User name is required.")
                .MaximumLength(100).WithMessage("User name is too long.");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required.")
                .MaximumLength(128).WithMessage("Password is too long.");

            // Not required: RecaptchaVerifier decides whether a token is needed for this
            // environment, and duplicating that decision here would break local runs.
            RuleFor(x => x.RecaptchaToken)
                .MaximumLength(4096).WithMessage("Recaptcha token is too long.");
        }
    }

    public sealed class GoogleCodeExchangeRequestValidator : AbstractValidator<GoogleCodeExchangeRequest>
    {
        public GoogleCodeExchangeRequestValidator()
        {
            RuleFor(x => x.Code)
                .NotEmpty().WithMessage("Authorization code is required.")
                .MaximumLength(2048).WithMessage("Authorization code is too long.");

            RuleFor(x => x.RedirectUri)
                .NotEmpty().WithMessage("Redirect uri is required.")
                .MaximumLength(2048).WithMessage("Redirect uri is too long.")
                .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out _))
                    .WithMessage("Redirect uri must be an absolute uri.");

            RuleFor(x => x.CodeVerifier)
                .NotEmpty().WithMessage("Code verifier is required.")
                .Length(43, 128).WithMessage("Code verifier must be between 43 and 128 characters.");
        }
    }

    public sealed class RequestPasswordResetRequestValidator : AbstractValidator<UserController.RequestPasswordResetRequest>
    {
        public RequestPasswordResetRequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("Email is invalid.")
                .MaximumLength(200).WithMessage("Email is too long.");

            RuleFor(x => x.RecaptchaToken)
                .MaximumLength(4096).WithMessage("Recaptcha token is too long.");
        }
    }

    public sealed class ResetPasswordRequestValidator : AbstractValidator<UserController.ResetPasswordRequest>
    {
        public ResetPasswordRequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("Email is invalid.");

            RuleFor(x => x.Token)
                .NotEmpty().WithMessage("Reset token is required.")
                .MaximumLength(512).WithMessage("Reset token is too long.");

            RuleFor(x => x.NewPassword)
                .NotEmpty().WithMessage("New password is required.")
                .MinimumLength(8).WithMessage("New password must have at least 8 characters.")
                .MaximumLength(128).WithMessage("New password is too long.");
        }
    }

    public sealed class UpdateThemeRequestValidator : AbstractValidator<UserController.UpdateThemeRequest>
    {
        public UpdateThemeRequestValidator()
        {
            // The theme is an opaque client preference with no server-side enum, so this only keeps
            // it in a sane range rather than pretending to know the palette.
            RuleFor(x => x.Theme)
                .InclusiveBetween((short)0, (short)10).WithMessage("Theme is not supported.");
        }
    }

    public sealed class UpdateLanguageRequestValidator : AbstractValidator<UserController.UpdateLanguageRequest>
    {
        public UpdateLanguageRequestValidator()
        {
            RuleFor(x => x.Language)
                .Must(language => Enum.IsDefined(typeof(EnumLanguage), language))
                .WithMessage("Language is not supported.");
        }
    }

    public sealed class UpdateProfilePictureRequestValidator : AbstractValidator<UserController.UpdateProfilePictureRequest>
    {
        private const int MaxProfilePictureSizeInBytes = 5 * 1024 * 1024;

        public UpdateProfilePictureRequestValidator()
        {
            // Optional: an empty payload clears the stored picture.
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
