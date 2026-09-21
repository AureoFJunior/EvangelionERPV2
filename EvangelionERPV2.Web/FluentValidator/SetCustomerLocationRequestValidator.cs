using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    /// <summary>
    /// A hand-placed pin, checked at the boundary.
    /// </summary>
    public sealed class SetCustomerLocationRequestValidator : AbstractValidator<SetCustomerLocationRequestDTO>
    {
        public SetCustomerLocationRequestValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty().WithMessage("CustomerId is required.");
            RuleFor(x => x.Latitude).InclusiveBetween(-90m, 90m).WithMessage("Latitude is out of range.");
            RuleFor(x => x.Longitude).InclusiveBetween(-180m, 180m).WithMessage("Longitude is out of range.");
        }
    }
}
