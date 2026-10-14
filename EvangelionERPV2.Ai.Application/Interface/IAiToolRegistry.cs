using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Interface
{
    public interface IAiToolRegistry
    {
        IReadOnlyList<AIFunction> Build(IReadOnlySet<string> grantedPermissions, AiToolContext context);

        IAiTool? Find(string name);
    }
}
