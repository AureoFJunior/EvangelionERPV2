using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class UpdateEmployeeAbsenceRequestValidator : AbstractValidator<UpdateEmployeeAbsenceRequestDTO>
    {
        public UpdateEmployeeAbsenceRequestValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty().WithMessage("Employee absence id is required.");

            RuleFor(x => x.Type)
                .Must(type => Enum.IsDefined(typeof(EnumEmployeeAbsenceType), type))
                .WithMessage("Employee absence type is invalid.");

            RuleFor(x => x.StartDate)
                .NotEmpty().WithMessage("Employee absence start date is required.");

            RuleFor(x => x.EndDate)
                .NotEmpty().WithMessage("Employee absence end date is required.")
                .GreaterThanOrEqualTo(x => x.StartDate)
                    .WithMessage("Employee absence end date cannot be before the start date.")
                .Must((request, endDate) => (endDate.Date - request.StartDate.Date).TotalDays <= MaxAbsenceDays)
                    .WithMessage("Employee absence cannot span more than a year.");

            RuleFor(x => x.Reason)
                .MaximumLength(500).WithMessage("Employee absence reason is too long.");

            RuleFor(x => x.Cid)
                .MaximumLength(20).WithMessage("Employee absence diagnosis code is too long.")
                .Must((request, _) => request.Type == (int)EnumEmployeeAbsenceType.Atestado)
                    .When(x => !string.IsNullOrWhiteSpace(x.Cid))
                    .WithMessage("A diagnosis code can only be recorded on an atestado.");
        }

        private const int MaxAbsenceDays = 366;
    }
}
