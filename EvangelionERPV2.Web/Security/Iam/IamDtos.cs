using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.Web.Security.Iam
{
    public sealed record CatalogModuleDTO(string Key, string Label, IReadOnlyList<CatalogPermissionDTO> Permissions);

    public sealed record CatalogPermissionDTO(string Permission, string Label, string Tier, bool Grantable, bool Baseline);

    public sealed record UserAccessDTO(Guid UserId, string Name, EnumIamMode Mode, bool MfaEnabled, short AccessLevel, IReadOnlyList<AccessModuleDTO> Modules);

    public sealed record AccessModuleDTO(string Key, string Label, IReadOnlyList<AccessPermissionDTO> Permissions);

    /// <summary>
    /// One leaf of a user's tree. <c>Override</c>: "allow", "deny" or null (follows the role / holds nothing).
    /// <c>Effective</c>: whether the user holds it now, in the enterprise's mode. <c>HeldByRole</c>: what the role gives.
    /// </summary>
    public sealed record AccessPermissionDTO(
        string Permission, string Label, string Tier, bool Grantable, bool Baseline,
        string? Override, bool Effective, DateTime? ExpiresAt, Guid? GrantedBy, string? Reason, DateTime? LastUsedAt, bool HeldByRole);

    public sealed record AccessReviewDTO(IReadOnlyList<UnusedGrantDTO> Unused, IReadOnlyList<UnknownGrantDTO> Unknown, IReadOnlyList<MfaGapDTO> WithoutMfa);

    /// <summary><c>Bucket</c>: 30, 60 or 90 (90 meaning 90 or more days without use).</summary>
    public sealed record UnusedGrantDTO(Guid UserId, string Name, string Permission, DateTime? LastUsedAt, DateTime GrantedAt, int IdleDays, int Bucket);

    public sealed record UnknownGrantDTO(Guid UserId, string Name, string Permission);

    public sealed record MfaGapDTO(Guid UserId, string Name, IReadOnlyList<string> Permissions);

    public sealed record HolderDTO(Guid UserId, string Name, string Source, DateTime? ExpiresAt);

    /// <summary><c>Holds</c>: what the caller has. <c>Usable</c>: what works right now, step-up included.</summary>
    public sealed record MyAccessDTO(
        EnumIamMode Mode, bool MfaEnabled, IReadOnlyList<string> Holds, IReadOnlyList<string> Usable,
        IReadOnlyDictionary<string, string> Tiers, DateTimeOffset? SensitiveUntil, DateTimeOffset? PrivilegedUntil);

    public sealed record StepUpDTO(DateTimeOffset VerifiedAt, DateTimeOffset SensitiveUntil, DateTimeOffset PrivilegedUntil);

    public sealed class StepUpRequest
    {
        public string? Code { get; set; }
    }

    public sealed class GrantChangeRequest
    {
        public string? Permission { get; set; }
        public string? Module { get; set; }

        /// <summary>"allow", "deny" or "inherit".</summary>
        public string? State { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class UpdateGrantsRequest
    {
        public List<GrantChangeRequest> Changes { get; set; } = [];
    }

    public sealed class ApplyTemplateRequest
    {
        public EnumAccessLevel Template { get; set; }
        public bool Replace { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class SwitchIamModeRequest
    {
        public EnumIamMode Mode { get; set; }
    }
}
