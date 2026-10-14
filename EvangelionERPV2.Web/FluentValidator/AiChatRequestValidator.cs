using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class AiChatRequestValidator : AbstractValidator<AiChatRequestDTO>
    {
        public AiChatRequestValidator()
        {
            RuleFor(x => x.Message).NotEmpty().MaximumLength(2000);
            RuleFor(x => x.ConversationId).NotEqual(Guid.Empty).When(x => x.ConversationId.HasValue);
        }
    }
}
