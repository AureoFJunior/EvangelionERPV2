using System.Security.Claims;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Web.Security
{
    /// <summary><c>MfaRequired</c>: held, but the user has no MFA. <c>StepUpRequired</c>: held, but no fresh MFA code on this token.</summary>
    public enum PermissionDecision { Allowed, Denied, MfaRequired, StepUpRequired }

    /// <summary>
    /// The one place access is decided. Every authorization handler, and every controller that filters
    /// by permission, asks this, so changing where permissions come from changes every gate at once.
    /// </summary>
    public interface IPermissionEvaluator
    {
        Task<PermissionDecision> DecideAsync(ClaimsPrincipal principal, User user, string permission);

        /// <summary>What <paramref name="user"/> may exercise through this caller's token right now, step-up included.</summary>
        Task<IReadOnlySet<string>> EffectiveAsync(ClaimsPrincipal principal, User user);

        /// <summary>What <paramref name="user"/> holds through this token, before step-up: for showing, not for acting.</summary>
        Task<IReadOnlySet<string>> HoldsAsync(ClaimsPrincipal principal, User user);

        /// <summary>What each user holds regardless of any token, for listing other users (a till's operators).</summary>
        Task<IReadOnlyDictionary<Guid, IReadOnlySet<string>>> GrantedAsync(IReadOnlyCollection<User> users);
    }
}
