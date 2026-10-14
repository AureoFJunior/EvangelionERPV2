using System.Security.Claims;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.AspNetCore.Authorization;

namespace EvangelionERPV2.Web.Security
{
    public sealed class ActiveTenantEnforcementMiddleware
    {
        private readonly RequestDelegate _next;

        public ActiveTenantEnforcementMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            IRepository<User> userRepository,
            IRepository<Enterprise> enterpriseRepository)
        {
            var endpoint = context.GetEndpoint();
            var allowsAnonymous = endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null;
            var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;

            if (!isAuthenticated || allowsAnonymous)
            {
                await _next(context);
                return;
            }

            var userId = ResolveUserId(context.User!);
            var enterpriseId = ResolveEnterpriseId(context.User!);
            if (!userId.HasValue || !enterpriseId.HasValue)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            User? user;
            Enterprise? enterprise;
            try
            {
                // Repositories throw NotFoundDatabaseException when the row is gone (e.g. a still-valid
                // JWT whose user/enterprise was deleted). Treat that as an invalid token (401) rather than
                // letting the outer exception middleware map it to a misleading 204 No Content.
                user = await userRepository.GetByIdAsync(userId.Value);
                enterprise = user == null ? null : await enterpriseRepository.GetByIdAsync(enterpriseId.Value);
            }
            catch (NotFoundDatabaseException)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (user == null || user.IsActive != true || user.EnterpriseId != enterpriseId.Value)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (enterprise == null || enterprise.IsActive != true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await _next(context);
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
