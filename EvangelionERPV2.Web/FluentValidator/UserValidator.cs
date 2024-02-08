using EvangelionERPV2.Domain.Models;
using EvangelionERPV2.Domain.Models;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public class UserValidator : AbstractValidator<User>
    {
        public UserValidator() 
        {
            RuleFor(user => user.FirstName)
            .NotEmpty().WithMessage("FirstName must be not empty")
            .Must(firstName => !string.IsNullOrEmpty(firstName)).WithMessage("FirstName must be not empty");
        }
    }
}
