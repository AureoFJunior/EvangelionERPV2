using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Interface
{
    public sealed record AiToolContext(Guid EnterpriseId, Guid UserId);

    /// <summary>
    /// The module's own copy of the two RBAC strings it cares about. `Web.Security.RbacPermissions`
    /// is the authority; the tools already spell their permissions as literals for the same reason
    /// (this project does not reference the host).
    /// </summary>
    public static class AiPermissions
    {
        public const string Chat = "ai.chat";
        public const string Act = "ai.act";
    }

    public interface IAiTool
    {
        string Name { get; }

        string Description { get; }

        string RequiredPermission { get; }

        bool RequiresConfirmation { get; }

        AIFunction Create(AiToolContext context);

        /// <summary>
        /// One pt-BR line for the confirmation card — what this call will actually do, in the
        /// caller's words rather than the model's. Only writes need to say anything.
        /// </summary>
        string Summarize(IReadOnlyDictionary<string, object?> arguments) => Name;
    }
}
