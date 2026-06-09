using System.Security.Claims;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace EvangelionERPV2.Web.Security
{
    public sealed class SelfOrPermissionAuthorizationHandler : AuthorizationHandler<SelfOrPermissionRequirement>
    {
        private readonly IRepository<User> _userRepository;

        public SelfOrPermissionAuthorizationHandler(IRepository<User> userRepository)
        {
            _userRepository = userRepository;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            SelfOrPermissionRequirement requirement)
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

            if (RbacRolePermissionMap.HasPermission(user.AccessLevel, requirement.ElevatedPermission))
            {
                context.Succeed(requirement);
                return;
            }

            if (!RbacRolePermissionMap.HasPermission(user.AccessLevel, requirement.SelfPermission))
                return;

            var targetUserId = ResolveRouteUserId(context.Resource, requirement.RouteKey);
            if (!targetUserId.HasValue)
                return;

            if (targetUserId.Value == user.Id)
                context.Succeed(requirement);
        }

        private static Guid? ResolveRouteUserId(object? resource, string routeKey)
        {
            if (resource is AuthorizationFilterContext filterContext)
            {
                if (TryGetRouteValueAsGuid(filterContext.RouteData.Values, routeKey, out var id))
                    return id;
            }

            if (resource is HttpContext httpContext)
            {
                if (TryGetRouteValueAsGuid(httpContext.GetRouteData()?.Values, routeKey, out var id))
                    return id;
            }

            return null;
        }

        private static bool TryGetRouteValueAsGuid(RouteValueDictionary? routeValues, string routeKey, out Guid value)
        {
            value = Guid.Empty;
            if (routeValues == null || !routeValues.TryGetValue(routeKey, out var rawValue) || rawValue == null)
                return false;

            return Guid.TryParse(rawValue.ToString(), out value) && value != Guid.Empty;
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
