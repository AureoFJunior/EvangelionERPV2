using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
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

            RuleFor(x => x.Channel)
                .Must(channel => channel is null || Enum.IsDefined(typeof(EnumOrderChannel), channel.Value))
                .WithMessage("Channel must be 0 (ERP), 1 (PDV) or 2 (Limen).");
        }
    }
}
