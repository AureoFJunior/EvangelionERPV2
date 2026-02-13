using EvangelionERPV2.Shared.Entities;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class ProductPictureValidator : AbstractValidator<ProductPicture>
    {
        public ProductPictureValidator(IValidator<Product> productValidator)
        {
            RuleFor(x => x.Product)
                .NotNull().WithMessage("Product must be filled")
                .SetValidator(productValidator);

            RuleFor(x => x.File)
                .NotEmpty().WithMessage("Product image must be filled");
        }
    }
}
