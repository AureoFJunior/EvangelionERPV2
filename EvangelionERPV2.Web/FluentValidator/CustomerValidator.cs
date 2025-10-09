using EvangelionERPV2.Shared.Entities;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public class CustomerValidator : AbstractValidator<Customer>
    {
        public CustomerValidator() 
        {
            RuleFor(customer => customer)
            .NotNull().WithMessage("Customer must be filled")
            .Must(fields => !string.IsNullOrEmpty(fields.Name)).WithMessage("Customer must have a Name");

            RuleFor(customer => customer)
            .NotNull().WithMessage("Customer must be filled")
            .Must(fields => fields.EnterpriseId != null && fields.EnterpriseId != default(Guid)).WithMessage("Customer must have a Enterprise");
        }
    }
}   
