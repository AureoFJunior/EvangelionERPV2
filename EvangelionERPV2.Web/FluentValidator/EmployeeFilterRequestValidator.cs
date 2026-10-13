using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class EmployeeFilterRequestValidator : AbstractValidator<EmployeeFilterRequestDTO>
    {
        public EmployeeFilterRequestValidator()
        {
            RuleFor(x => x.Name)
                .MaximumLength(150).WithMessage("Name filter is too long.");

            RuleFor(x => x.Email)
                .MaximumLength(150).WithMessage("Email filter is too long.");

            RuleFor(x => x.Position)
                .MaximumLength(100).WithMessage("Position filter is too long.");

            RuleFor(x => x.PhoneNumber)
                .MaximumLength(30).WithMessage("Phone filter is too long.");
        }
    }
}
