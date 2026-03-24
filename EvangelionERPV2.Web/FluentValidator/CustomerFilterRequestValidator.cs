using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class CustomerFilterRequestValidator : AbstractValidator<CustomerFilterRequestDTO>
    {
        public CustomerFilterRequestValidator()
        {
            RuleFor(x => x.Name).MaximumLength(150).When(x => !string.IsNullOrWhiteSpace(x.Name));
            RuleFor(x => x.Email).MaximumLength(150).When(x => !string.IsNullOrWhiteSpace(x.Email));
            RuleFor(x => x.Document).MaximumLength(30).When(x => !string.IsNullOrWhiteSpace(x.Document));
            RuleFor(x => x.PhoneNumber).MaximumLength(30).When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        }
    }
}
