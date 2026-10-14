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

    /// <summary>Arguments ready to store, or why they cannot be.</summary>
    public sealed record AiToolArguments(IReadOnlyDictionary<string, object?> Arguments, string? Error)
    {
        public static AiToolArguments Ok(IReadOnlyDictionary<string, object?> arguments) => new(arguments, null);

        public static AiToolArguments Refused(string error) =>
            new(new Dictionary<string, object?>(), error);
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
        /// Checks and, where it can, repairs the arguments before a write becomes a confirmation
        /// card. Two jobs, both aimed at the same failure: a local model that truncates an id or
        /// sends a name where an id belongs. Anything it cannot repair comes back as an error the
        /// model reads while it is still answering, rather than a card that fails on confirm.
        /// The tool re-checks everything when it actually runs — these arguments are replayed from
        /// storage minutes later.
        /// </summary>
        Task<AiToolArguments> PrepareAsync(
            IReadOnlyDictionary<string, object?> arguments,
            AiToolContext context,
            CancellationToken cancellation = default) => Task.FromResult(AiToolArguments.Ok(arguments));
    }
}
