using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class ProductFilterRequestValidator : AbstractValidator<ProductFilterRequestDTO>
    {
        public ProductFilterRequestValidator()
        {
            RuleFor(x => x.Name)
                .MaximumLength(150)
                .When(x => !string.IsNullOrWhiteSpace(x.Name))
                .WithMessage("Name filter is too long.");
        }
    }
}
