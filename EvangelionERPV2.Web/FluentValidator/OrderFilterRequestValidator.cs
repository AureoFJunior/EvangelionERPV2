using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class OrderFilterRequestValidator : AbstractValidator<OrderFilterRequestDTO>
    {
        public OrderFilterRequestValidator()
        {
            RuleFor(x => x)
                .Must(x => !x.StartDate.HasValue || !x.EndDate.HasValue || x.EndDate.Value >= x.StartDate.Value)
                .WithMessage("EndDate must be greater than or equal to StartDate.");
        }
    }
}
