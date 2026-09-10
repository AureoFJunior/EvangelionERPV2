using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Interface
{
    public sealed record AiToolContext(Guid EnterpriseId, Guid UserId);

    public interface IAiTool
    {
        string Name { get; }

        string Description { get; }

        string RequiredPermission { get; }

        bool RequiresConfirmation { get; }

        AIFunction Create(AiToolContext context);
    }
}
