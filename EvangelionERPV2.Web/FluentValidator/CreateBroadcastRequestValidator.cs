using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    /// <summary>
    /// A broadcast reaches every person in the company and cannot be edited afterwards, only
    /// retired — so the shape is checked before it is written, not after someone reports it.
    /// </summary>
    public sealed class CreateBroadcastRequestValidator : AbstractValidator<CreateBroadcastDTO>
    {
        public CreateBroadcastRequestValidator()
        {
            RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
            RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
            RuleFor(x => x.Severity).IsInEnum();

            RuleFor(x => x.ExpiresAt)
                .GreaterThan(_ => DateTime.UtcNow)
                .When(x => x.ExpiresAt.HasValue);
        }
    }
}
