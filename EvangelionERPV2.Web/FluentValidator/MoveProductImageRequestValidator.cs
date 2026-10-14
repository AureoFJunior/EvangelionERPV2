using EvangelionERPV2.ProductModule.Application.Services;
using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class MoveProductImageRequestValidator : AbstractValidator<MoveProductImageRequestDTO>
    {
        public MoveProductImageRequestValidator()
        {
            RuleFor(x => x.Position)
                .InclusiveBetween(0, ProductService.MaxImagesPerProduct - 1)
                .WithMessage($"Position must be between 0 and {ProductService.MaxImagesPerProduct - 1}.");
        }
    }
}
