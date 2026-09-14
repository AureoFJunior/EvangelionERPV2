using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    /// <summary>
    /// The 600 here is the same backstop as <c>AiSettings.MaxSpeechChars</c>, which the controller
    /// also checks — the client splits an answer into sentences, so anything near the cap means the
    /// splitter let a paragraph through.
    /// </summary>
    public sealed class AiSpeakRequestValidator : AbstractValidator<AiSpeakRequestDTO>
    {
        public AiSpeakRequestValidator()
        {
            RuleFor(x => x.Text).NotEmpty().MaximumLength(600);
        }
    }
}
