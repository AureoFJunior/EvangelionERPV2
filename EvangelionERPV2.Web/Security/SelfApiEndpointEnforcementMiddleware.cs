using EvangelionERPV2.Shared.Security;
using Microsoft.AspNetCore.Authorization;

namespace EvangelionERPV2.Web.Security;

/// <summary>Limits the shared worker identity to actions explicitly marked for workers.</summary>
public sealed class SelfApiEndpointEnforcementMiddleware
{
    private readonly RequestDelegate _next;

    public SelfApiEndpointEnforcementMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var isSelfApi = context.User.HasClaim(MachineClaims.ClientType, MachineClaims.SelfApiValue);
        var hasWorkerPolicy = context.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(data => data.Policy == RbacPolicies.Machine.SelfApiWorker) == true;

        if (isSelfApi && !hasWorkerPolicy)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }
}
