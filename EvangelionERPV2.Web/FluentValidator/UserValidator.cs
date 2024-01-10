using EvangelionERPV2.Domain.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public class UserValidator : AbstractValidator<UserDTO>
    {
        public UserValidator() 
        {
            RuleFor(customer => customer.FirstName).Must(x => !string.IsNullOrEmpty(x)).WithMessage("FirstName must be not empty");
        }
    }
}
