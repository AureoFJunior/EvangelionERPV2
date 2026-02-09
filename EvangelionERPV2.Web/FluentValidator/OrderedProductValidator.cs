using EvangelionERPV2.Shared.Entities;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public class OrderedProductValidator : AbstractValidator<OrderedProduct>
    {
        public OrderedProductValidator() 
        {
            RuleFor(orderedProduct => orderedProduct)
            .NotNull().WithMessage("Ordered Product must be filled")
            .Must(fields => fields.ProductId != Guid.Empty).WithMessage("Ordered Product must have an ProductId");

            RuleFor(orderedProduct => orderedProduct)
            .NotNull().WithMessage("Ordered Product must be filled")
            .Must(fields => fields.Value > 0 && fields.Quantity > 0).WithMessage("Ordered Product must have some Quantity and Value");
        }
    }
}   
