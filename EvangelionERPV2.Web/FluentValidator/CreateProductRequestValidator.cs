using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequestDTO>
    {
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
                .NotEmpty().WithMessage("Product image file is required.");
        }
    }
}
