using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Interface
{
    /// <summary>
    /// What one tool call knows about who is asking. EnterpriseId is here for the tools that take
    /// it as a service argument; it is never a model-supplied parameter, and the AppDbContext
    /// global filter would ignore it anyway.
    /// </summary>
    public sealed record AiToolContext(Guid EnterpriseId, Guid UserId);

    public interface IAiTool
    {
        /// <summary>The name the model sees, in pt-BR: buscar_produtos, not search_products.</summary>
        string Name { get; }

        /// <summary>pt-BR. This is prompt text, and it is the main lever on tool-choice accuracy.</summary>
        string Description { get; }

        /// <summary>An RbacPermissions constant. The registry never surfaces a tool without it.</summary>
        string RequiredPermission { get; }

        /// <summary>False for every Rung 1 tool. Rung 2 sets it on the four write tools.</summary>
        bool RequiresConfirmation { get; }

        AIFunction Create(AiToolContext context);
    }
}
