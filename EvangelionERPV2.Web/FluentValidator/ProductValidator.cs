using EvangelionERPV2.Shared.Entities;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public class ProductValidator : AbstractValidator<Product>
    {
        public ProductValidator() 
        {
            RuleFor(product => product)
            .NotNull().WithMessage("Product must be filled")
            .Must(fields => !string.IsNullOrEmpty(fields.Name)).WithMessage("Product must have a Name");

            RuleFor(product => product)
            .NotNull().WithMessage("Product must be filled")
            .Must(fields => fields.DefaultValue > 0).WithMessage("Product must have a Defaul Value");

            RuleFor(product => product)
            .NotNull().WithMessage("Product must be filled")
            .Must(fields => fields.StorageQuantity > 0).WithMessage("Product must have a Storage Quantity");
        }
    }
}
