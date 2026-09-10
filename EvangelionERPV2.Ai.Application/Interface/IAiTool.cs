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

        /// <summary>
        /// Why these arguments cannot work, or null when they can. Checked before a write becomes a
        /// confirmation card, so the model gets the correction while it is still answering instead
        /// of a human confirming something that was always going to fail. Cheap checks only — shape,
        /// not existence; the tool re-checks everything when it actually runs.
        /// </summary>
        string? Validate(IReadOnlyDictionary<string, object?> arguments) => null;
    }
}
