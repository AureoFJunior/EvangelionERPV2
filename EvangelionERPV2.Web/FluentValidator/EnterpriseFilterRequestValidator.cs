using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class EnterpriseFilterRequestValidator : AbstractValidator<EnterpriseFilterRequestDTO>
    {
        public EnterpriseFilterRequestValidator()
        {
            RuleFor(x => x.Name).MaximumLength(150).When(x => !string.IsNullOrWhiteSpace(x.Name));
        }
    }
}
