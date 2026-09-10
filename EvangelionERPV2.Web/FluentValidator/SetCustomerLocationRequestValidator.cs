using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    /// <summary>
    /// A hand-placed pin, checked at the boundary.
    /// </summary>
    /// <remarks>
    /// The same range lives in <c>CustomerLocationService.SetLocationAsync</c> and is not duplicated
    /// by accident: this one turns a bad body into a 400 before anything runs, and that one holds
    /// the invariant for every caller the HTTP boundary does not cover. A point off the globe is not
    /// a typo worth storing either way.
    /// </remarks>
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
