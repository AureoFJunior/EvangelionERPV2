using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class UploadProductPictureRequestValidator : AbstractValidator<UploadProductPictureRequestDTO>
    {
        public UploadProductPictureRequestValidator()
        {
            RuleFor(x => x.ProductId)
                .NotEmpty().WithMessage("ProductId is required.");

            RuleFor(x => x.File)
                .NotEmpty().WithMessage("File is required.");
        }
    }
}
