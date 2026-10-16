using EvangelionERPV2.Web.Security.Iam;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class UpdateGrantsRequestValidator : AbstractValidator<UpdateGrantsRequest>
    {
        public UpdateGrantsRequestValidator()
        {
            RuleFor(x => x.Changes).NotEmpty().Must(c => c.Count <= 200).WithMessage("Send between 1 and 200 changes.");
            RuleForEach(x => x.Changes).ChildRules(change =>
            {
                change.RuleFor(c => c).Must(c => string.IsNullOrWhiteSpace(c.Permission) != string.IsNullOrWhiteSpace(c.Module))
                    .WithMessage("Each change names a permission or a module, not both.");
                change.RuleFor(c => c.Permission).MaximumLength(64);
                change.RuleFor(c => c.Module).MaximumLength(64);
                change.RuleFor(c => c.Reason).MaximumLength(280);
                change.RuleFor(c => c.State)
                    .Must(state => state is "allow" or "deny" or "inherit")
                    .WithMessage("State must be allow, deny or inherit.");
            });
        }
    }

    public sealed class ApplyTemplateRequestValidator : AbstractValidator<ApplyTemplateRequest>
    {
        public ApplyTemplateRequestValidator()
        {
            RuleFor(x => x.Template).IsInEnum();
            RuleFor(x => x.Reason).NotEmpty().MaximumLength(280);
        }
    }

    public sealed class StepUpRequestValidator : AbstractValidator<StepUpRequest>
    {
        public StepUpRequestValidator()
        {
            RuleFor(x => x.Code).NotEmpty().MaximumLength(16);
        }
    }

    public sealed class SwitchIamModeRequestValidator : AbstractValidator<SwitchIamModeRequest>
    {
        public SwitchIamModeRequestValidator()
        {
            RuleFor(x => x.Mode).IsInEnum();
        }
    }
}
