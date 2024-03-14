using EvangelionERPV2.Domain.Models;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public class EnterpriseValidator : AbstractValidator<Enterprise>
    {
        public EnterpriseValidator() 
        {
            RuleFor(user => user.Adress)
            .NotEmpty().WithMessage("Adress must be not empty")
            .Must(firstName => !string.IsNullOrEmpty(firstName)).WithMessage("Adress must be not empty");

            RuleFor(user => user.Name)
            .NotEmpty().WithMessage("Name must be not empty")
            .Must(firstName => !string.IsNullOrEmpty(firstName)).WithMessage("Name must be not empty");
        }
    }
}   