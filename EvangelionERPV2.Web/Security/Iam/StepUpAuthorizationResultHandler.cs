using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EvangelionERPV2.Web.Security.Iam
{
    /// <summary>
    /// Turns "held, but needs MFA or a fresh code" into a 403 the client can act on, instead of a bare one:
    /// <c>{ code: "mfa_required" | "step_up_required", permission, windowMinutes }</c>. Not 401: the client
    /// answers a 401 by refreshing the token, and a refreshed token has a new jti, so the prompt would never show.
    /// </summary>
    public sealed class StepUpAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private const string ItemKey = "iam.stepup";
        private readonly AuthorizationMiddlewareResultHandler _default = new();

        public static void Remember(AuthorizationHandlerContext context, PermissionDecision decision, string permission)
        {
            if (decision is not (PermissionDecision.MfaRequired or PermissionDecision.StepUpRequired))
                return;

            var http = context.Resource switch
            {
                HttpContext h => h,
                AuthorizationFilterContext f => f.HttpContext,
                _ => null
            };
            if (http != null && !http.Items.ContainsKey(ItemKey))
                http.Items[ItemKey] = (decision, permission);
        }

        public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            if (authorizeResult.Forbidden && context.Items.TryGetValue(ItemKey, out var raw) && raw is (PermissionDecision decision, string permission))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = decision == PermissionDecision.MfaRequired ? "mfa_required" : "step_up_required",
                    message = decision == PermissionDecision.MfaRequired
                        ? "Ative a verificação em duas etapas para usar esta função."
                        : "Confirme com o código do seu aplicativo autenticador para continuar.",
                    permission,
                    windowMinutes = (int)StepUp.Window(IamCatalog.TierOf(permission)).TotalMinutes
                }, cancellationToken: context.RequestAborted);
                return;
            }

            await _default.HandleAsync(next, context, policy, authorizeResult);
        }
    }
}
