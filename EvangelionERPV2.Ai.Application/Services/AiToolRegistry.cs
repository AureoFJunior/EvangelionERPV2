using EvangelionERPV2.AiModule.Application.Interface;
using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Services
{
    public sealed class AiToolRegistry : IAiToolRegistry
    {
        private readonly IReadOnlyDictionary<string, IAiTool> _tools;

        public AiToolRegistry(IEnumerable<IAiTool> tools)
        {
            _tools = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        }

        public IReadOnlyList<AIFunction> Build(IReadOnlySet<string> grantedPermissions, AiToolContext context) =>
            _tools.Values
                .Where(tool => grantedPermissions.Contains(tool.RequiredPermission))
                // A write tool needs ai.act on top of its own permission. Without it the tool is not
                // in the list at all, so the model cannot be talked into proposing the write.
                .Where(tool => !tool.RequiresConfirmation || grantedPermissions.Contains(AiPermissions.Act))
                .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                .Select(tool => tool.Create(context))
                .ToList();

        public IAiTool? Find(string name) =>
            _tools.TryGetValue(name, out var tool) ? tool : null;
    }
}
