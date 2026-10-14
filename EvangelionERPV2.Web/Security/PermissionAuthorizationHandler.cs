using System.Security.Claims;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.AspNetCore.Authorization;

namespace EvangelionERPV2.Web.Security
{
    public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IRepository<User> _userRepository;
        private readonly IPermissionEvaluator _permissions;

        public PermissionAuthorizationHandler(IRepository<User> userRepository, IPermissionEvaluator permissions)
        {
            _userRepository = userRepository;
            _permissions = permissions;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            if (context.User?.Identity?.IsAuthenticated != true)
                return;

            var userId = ResolveUserId(context.User);
            var enterpriseId = ResolveEnterpriseId(context.User);
            if (!userId.HasValue || !enterpriseId.HasValue)
                return;

            var user = await _userRepository.GetByIdAsync(userId.Value);
            if (user == null || user.IsActive != true || user.EnterpriseId != enterpriseId.Value)
                return;

            var decision = await _permissions.DecideAsync(context.User, user, requirement.Permission);
            if (decision == PermissionDecision.Allowed)
                context.Succeed(requirement);
            else
            {
                Iam.IamMetrics.Denied.WithLabels(Iam.IamMetrics.ReasonOf(decision)).Inc();
                Iam.StepUpAuthorizationResultHandler.Remember(context, decision, requirement.Permission);
            }
        }

        private static Guid? ResolveUserId(ClaimsPrincipal user)
        {
            var claimValue = user.FindFirst(ClaimTypes.Sid)?.Value
                ?? user.FindFirst("uid")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (Guid.TryParse(claimValue, out var userId) && userId != Guid.Empty)
                return userId;

            return null;
        }

        private static Guid? ResolveEnterpriseId(ClaimsPrincipal user)
        {
            var claimValue = user.FindFirst(ClaimTypes.GroupSid)?.Value;

            if (Guid.TryParse(claimValue, out var enterpriseId) && enterpriseId != Guid.Empty)
                return enterpriseId;

            return null;
        }
    }
}
