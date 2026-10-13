using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    /// <summary>
    /// Bodies whose numbers drive query cost. Unbounded, a single request could ask for a decade of
    /// history or a page of a million rows.
    /// </summary>
    public sealed class RunSimulationRequestValidator : AbstractValidator<RunSimulationRequestDTO>
    {
        private const int MaxHorizonInDays = 365;
        private const int MaxScenarios = 20;

        public RunSimulationRequestValidator()
        {
            RuleFor(x => x.HorizonInDays)
                .InclusiveBetween(1, MaxHorizonInDays)
                .WithMessage($"Horizon must be between 1 and {MaxHorizonInDays} days.");

            RuleFor(x => x.Scenarios)
                .NotNull().WithMessage("At least one scenario is required.")
                .Must(scenarios => scenarios.Any()).WithMessage("At least one scenario is required.")
                .Must(scenarios => scenarios.Count() <= MaxScenarios)
                    .WithMessage($"At most {MaxScenarios} scenarios may be simulated at once.");

            RuleForEach(x => x.Scenarios).ChildRules(scenario =>
            {
                scenario.RuleFor(x => x.ScenarioName)
                    .NotEmpty().WithMessage("Scenario name is required.")
                    .MaximumLength(100).WithMessage("Scenario name is too long.");

                scenario.RuleFor(x => x.ReceivableDelayInDays)
                    .InclusiveBetween(-365, 365).WithMessage("Receivable delay is out of range.");

                scenario.RuleFor(x => x.PayableMultiplier)
                    .InclusiveBetween(0m, 100m).WithMessage("Payable multiplier is out of range.");
            });
        }
    }

    public sealed class OpportunityFeedbackRequestValidator : AbstractValidator<OpportunityFeedbackRequestDTO>
    {
        private static readonly string[] AllowedStatuses = ["accepted", "rejected", "ignored", "done"];

        public OpportunityFeedbackRequestValidator()
        {
            RuleFor(x => x.Status)
                .NotEmpty().WithMessage("Status is required.")
                .Must(status => AllowedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
                .WithMessage($"Status must be one of: {string.Join(", ", AllowedStatuses)}.");

            RuleFor(x => x.Comment)
                .MaximumLength(2000).WithMessage("Comment is too long.");

            RuleFor(x => x.MetadataJson)
                .MaximumLength(4000).WithMessage("Metadata is too long.");
        }
    }

    public sealed class OpportunityRecomputeRequestValidator : AbstractValidator<OpportunityRecomputeRequestDTO>
    {
        public OpportunityRecomputeRequestValidator()
        {
            RuleFor(x => x.HistoryWindowDays)
                .InclusiveBetween(1, 1095).WithMessage("History window must be between 1 and 1095 days.");
        }
    }

    public sealed class ReplenishmentSuggestionRequestValidator : AbstractValidator<ReplenishmentSuggestionRequestDTO>
    {
        public ReplenishmentSuggestionRequestValidator()
        {
            RuleFor(x => x.PageNumber)
                .GreaterThan(0).WithMessage("Page number must be greater than zero.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 200).WithMessage("Page size must be between 1 and 200.");

            RuleFor(x => x.HistoryWindowDays)
                .InclusiveBetween(1, 1095).WithMessage("History window must be between 1 and 1095 days.");
        }
    }

    public sealed class AuditTrailFilterValidator : AbstractValidator<AuditTrailFilterDTO>
    {
        public AuditTrailFilterValidator()
        {
            RuleFor(x => x.UserName).MaximumLength(100).WithMessage("User name filter is too long.");
            RuleFor(x => x.EntityName).MaximumLength(100).WithMessage("Entity name filter is too long.");
            RuleFor(x => x.Action).MaximumLength(50).WithMessage("Action filter is too long.");

            RuleFor(x => x.ChangedTo)
                .GreaterThanOrEqualTo(x => x.ChangedFrom!.Value)
                .When(x => x.ChangedFrom.HasValue && x.ChangedTo.HasValue)
                .WithMessage("The end of the period must not precede its start.");
        }
    }
}
