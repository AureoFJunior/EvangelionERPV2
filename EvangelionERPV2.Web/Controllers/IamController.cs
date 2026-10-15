using Asp.Versioning;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Security;
using EvangelionERPV2.UserModule.Application.Services;
using EvangelionERPV2.Web.Security.Iam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Serilog;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EvangelionERPV2.Web.Controllers
{
    /// <summary>Per-user access: the permission tree, grants, templates and the switch to zero trust (SPEC-iam-zero-trust §7).</summary>
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class IamController : Controller
    {
        private const int MaxChangesPerRequest = 200;
        private const int MaxRequestBodySizeInBytes = 64 * 1024;

        private readonly IamService _iam;
        private readonly IPermissionEvaluator _permissions;
        private readonly IRepository<User> _users;
        private readonly MfaService _mfa;
        private readonly IDistributedCache _cache;
        private readonly TimeProvider _time;

        public IamController(IamService iam, IPermissionEvaluator permissions, IRepository<User> users, MfaService mfa,
            IDistributedCache cache, TimeProvider? time = null)
        {
            _iam = iam;
            _permissions = permissions;
            _users = users;
            _mfa = mfa;
            _cache = cache;
            _time = time ?? TimeProvider.System;
        }

        /// <summary>Every permission, grouped by module, with its tier and whether it can be granted.</summary>
        [Authorize(Policy = RbacPolicies.Iam.Read)]
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<CatalogModuleDTO>), StatusCodes.Status200OK)]
        public IActionResult Catalog() =>
            Ok(IamCatalog.Modules.Select(m => new CatalogModuleDTO(m.Key, m.Label,
                m.Permissions.Select(p => new CatalogPermissionDTO(p.Permission, p.Label, IamService.TierName(p.Tier), p.Grantable, p.Baseline)).ToList())));

        /// <summary>One user's tree: each permission granted or not, until when, by whom and why.</summary>
        [Authorize(Policy = RbacPolicies.Iam.Read)]
        [HttpGet("{userId:guid}")]
        [ProducesResponseType(typeof(UserAccessDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public Task<IActionResult> Access(Guid userId) =>
            Run(async caller => Ok(await _iam.GetAccessAsync(caller.EnterpriseId!.Value, userId)));

        /// <summary>Grants or revokes permissions or whole modules in one atomic batch; answers the updated tree.</summary>
        [Authorize(Policy = RbacPolicies.Iam.Manage)]
        [HttpPut("{userId:guid}")]
        [RequestSizeLimit(MaxRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(UserAccessDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public Task<IActionResult> Grants(Guid userId, [FromBody] UpdateGrantsRequest request) =>
            Run(async caller =>
            {
                if (request == null || !ModelState.IsValid)
                    return BadRequest(new { code = "invalid_request", message = "A valid request body is required." });

                if (request.Changes.Count is 0 or > MaxChangesPerRequest)
                    return BadRequest(new { code = "invalid_changes", message = $"Envie de 1 a {MaxChangesPerRequest} alterações." });

                var changes = new List<GrantChange>(request.Changes.Count);
                foreach (var change in request.Changes)
                {
                    if (!TryParseState(change.State, out var state))
                        return BadRequest(new { code = "invalid_grant_state", message = "O estado da permissão deve ser allow, deny ou inherit." });

                    changes.Add(new GrantChange(change.Permission, change.Module, state, change.ExpiresAt?.ToUniversalTime(), change.Reason));
                }

                return Ok(await _iam.ApplyAsync(caller, userId, changes));
            });

        /// <summary>Writes a role's permissions as grants: merged with what the user has, or replacing it.</summary>
        [Authorize(Policy = RbacPolicies.Iam.Manage)]
        [HttpPost("{userId:guid}")]
        [ProducesResponseType(typeof(UserAccessDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public Task<IActionResult> ApplyTemplate(Guid userId, [FromBody] ApplyTemplateRequest request) =>
            Run(async caller =>
            {
                if (request == null || !ModelState.IsValid)
                    return BadRequest(new { code = "invalid_request", message = "A valid request body is required." });
                return Ok(await _iam.ApplyTemplateAsync(caller, userId, request.Template, request.Replace, request.Reason));
            });

        /// <summary>Moves the enterprise between role-based access and zero trust; needs MFA on the caller.</summary>
        [Authorize(Policy = RbacPolicies.Iam.Manage)]
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public Task<IActionResult> Switch([FromBody] SwitchIamModeRequest request) =>
            Run(async caller =>
            {
                if (request == null || !ModelState.IsValid)
                    return BadRequest(new { code = "invalid_request", message = "A valid request body is required." });
                return Ok(new { mode = await _iam.SwitchAsync(caller, request.Mode) });
            });

        /// <summary>Who holds a permission, and until when.</summary>
        [Authorize(Policy = RbacPolicies.Iam.Read)]
        [HttpGet("{permission}")]
        [ProducesResponseType(typeof(IEnumerable<HolderDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public Task<IActionResult> Holders(string permission) =>
            Run(async caller => Ok(await _iam.HoldersAsync(caller.EnterpriseId!.Value, permission)));

        /// <summary>Grants unused for 30/60/90+ days, grants to permissions that no longer exist, and sensitive holders without MFA.</summary>
        [Authorize(Policy = RbacPolicies.Iam.Read)]
        [HttpGet]
        [ProducesResponseType(typeof(AccessReviewDTO), StatusCodes.Status200OK)]
        public Task<IActionResult> Reviews() =>
            Run(async caller => Ok(await _iam.ReviewAsync(caller.EnterpriseId!.Value)));

        /// <summary>What the caller may do through this token; FrontV2 gates its screens on it instead of the role.</summary>
        [Authorize(Policy = RbacPolicies.Users.ReadSelf)]
        [HttpGet]
        [ProducesResponseType(typeof(MyAccessDTO), StatusCodes.Status200OK)]
        public Task<IActionResult> Me() =>
            Run(async caller =>
            {
                var holds = (await _permissions.HoldsAsync(User, caller)).OrderBy(p => p, StringComparer.Ordinal).ToList();
                var usable = (await _permissions.EffectiveAsync(User, caller)).OrderBy(p => p, StringComparer.Ordinal).ToList();
                var verifiedAt = caller.MfaEnabled ? await Security.Iam.StepUp.VerifiedAtAsync(_cache, User) : null;
                var now = _time.GetUtcNow();
                return Ok(new MyAccessDTO(
                    await _iam.ModeAsync(caller.EnterpriseId!.Value),
                    caller.MfaEnabled,
                    holds,
                    usable,
                    holds.ToDictionary(p => p, p => IamService.TierName(IamCatalog.TierOf(p))),
                    verifiedAt + Security.Iam.StepUp.SensitiveWindow is { } s && s > now ? s : null,
                    verifiedAt + Security.Iam.StepUp.PrivilegedWindow is { } p && p > now ? p : null));
            });

        /// <summary>
        /// Proves MFA for this token: sensitive permissions work for 15 minutes, privileged ones for 5. A
        /// refreshed token starts without the proof.
        /// </summary>
        [Authorize(Policy = RbacPolicies.Users.ReadSelf)]
        [HttpPost]
        [RequestSizeLimit(4 * 1024)]
        [ProducesResponseType(typeof(StepUpDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public Task<IActionResult> StepUp([FromBody] StepUpRequest request) =>
            Run(async caller =>
            {
                if (request == null || !ModelState.IsValid)
                    return BadRequest(new { code = "invalid_request", message = "A valid request body is required." });

                if (Security.Iam.StepUp.TokenId(User) is not { Length: > 0 } tokenId)
                {
                    IamMetrics.StepUps.WithLabels("no_token").Inc();
                    return BadRequest(new { code = "token_refresh_required", message = "Entre novamente para confirmar com o código." });
                }
                if (!caller.MfaEnabled)
                {
                    IamMetrics.StepUps.WithLabels("no_mfa").Inc();
                    return Conflict(new { code = "mfa_required", message = "Ative a verificação em duas etapas para confirmar ações sensíveis." });
                }
                if (!MfaService.IsWellFormedCode(request.Code) || !await _mfa.VerifyCodeAsync(caller.Id, request.Code))
                {
                    IamMetrics.StepUps.WithLabels("invalid").Inc();
                    return BadRequest(new { code = "mfa_invalid", message = "Código inválido. Confira o aplicativo autenticador." });
                }

                var now = _time.GetUtcNow();
                try
                {
                    await Security.Iam.StepUp.RecordAsync(_cache, tokenId, now);
                }
                catch (Exception ex)
                {
                    Log.Logger.Error("Step-up proof could not be stored. ErrorType={ErrorType}", ex.GetType().Name);
                    return StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                IamMetrics.StepUps.WithLabels("verified").Inc();
                return Ok(new StepUpDTO(now, now + Security.Iam.StepUp.SensitiveWindow, now + Security.Iam.StepUp.PrivilegedWindow));
            });

        /// <summary>Drops this token's step-up now ("lock").</summary>
        [Authorize(Policy = RbacPolicies.Users.ReadSelf)]
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> EndStepUp()
        {
            if (Security.Iam.StepUp.TokenId(User) is { Length: > 0 } tokenId)
                await Security.Iam.StepUp.EndAsync(_cache, tokenId);
            return NoContent();
        }

        private static bool TryParseState(string? state, out GrantState grantState)
        {
            switch (state?.Trim().ToLowerInvariant())
            {
                case "allow": grantState = GrantState.Allow; return true;
                case "deny": grantState = GrantState.Deny; return true;
                case "inherit": grantState = GrantState.Inherit; return true;
                default: grantState = default; return false;
            }
        }

        private async Task<IActionResult> Run(Func<User, Task<IActionResult>> action)
        {
            var caller = await CallerAsync();
            if (caller == null)
                return Unauthorized();

            try
            {
                return await action(caller);
            }
            catch (IamException ex)
            {
                return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
            }
        }

        private async Task<User?> CallerAsync()
        {
            var tenantClaim = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            var userClaim = User.FindFirst(ClaimTypes.Sid)?.Value ?? User.FindFirst("uid")?.Value;
            if (!Guid.TryParse(tenantClaim, out var enterpriseId) || !Guid.TryParse(userClaim, out var userId))
                return null;

            try
            {
                var user = await _users.GetByIdAsync(userId);
                return user is { IsActive: true } && user.EnterpriseId == enterpriseId ? user : null;
            }
            catch (NotFoundDatabaseException)
            {
                return null;
            }
        }
    }
}
