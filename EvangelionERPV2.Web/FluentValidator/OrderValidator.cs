using EvangelionERPV2.Shared.Entities;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public class OrderValidator : AbstractValidator<Order>
    {
        public OrderValidator() 
        {
            RuleFor(order => order)
            .NotNull().WithMessage("Order must be filled")
            .Must(fields => fields.EnterpriseId != null && fields.EnterpriseId != default(Guid) && fields.CustomerId != null).WithMessage("Order must have an Enterprise and/or an Customer");

            RuleFor(order => order)
            .NotNull().WithMessage("Order must be filled")
            .Must(fields => fields.OrderedProduct?.Any() == true).WithMessage("Order must have Ordered Products");
        }
    }
}   
