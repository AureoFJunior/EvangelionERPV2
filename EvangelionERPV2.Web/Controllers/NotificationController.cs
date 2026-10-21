using Asp.Versioning;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EvangelionERPV2.NotificationModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Hubs;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Security;
using EvangelionERPV2.Web.Security.Iam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Serilog;

namespace EvangelionERPV2.Web.Controllers
{
    /// <summary>
    /// The bell: what an admin wrote to everyone, plus what today's numbers are shouting about.
    /// </summary>
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class NotificationController : Controller
    {
        /// <summary>Long enough that a tenant whose alerts are steady is never re-pinged.</summary>
        private static readonly DistributedCacheEntryOptions AlertHashLifetime = new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24),
        };

        private const string AlertHashKeyPrefix = "eva:notif:alerts:";
        private const string NotificationsChanged = "NotificationsChanged";

        private readonly INotificationService _notificationService;
        private readonly IRepository<User> _users;
        private readonly IRepository<Enterprise> _enterprises;
        private readonly IHubContext<OrderHub> _hub;
        private readonly IDistributedCache _cache;
        private readonly IPermissionEvaluator? _permissionEvaluator;

        public NotificationController(
            INotificationService notificationService,
            IRepository<User> users,
            IRepository<Enterprise> enterprises,
            IHubContext<OrderHub> hub,
            IDistributedCache cache,
            IPermissionEvaluator? permissionEvaluator = null)
        {
            _notificationService = notificationService;
            _users = users;
            _enterprises = enterprises;
            _hub = hub;
            _cache = cache;
            _permissionEvaluator = permissionEvaluator;
        }

        /// <summary>Live broadcasts plus firing alerts, newest first, filtered to this caller.</summary>
        [Authorize(Policy = RbacPolicies.Notifications.Read)]
        [HttpGet]
        [ProducesResponseType(typeof(NotificationFeedDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Feed()
        {
            if (!TryGetCaller(out var enterpriseId, out var userId))
                return Unauthorized();

            var user = await TryGetCallerUserAsync(userId, enterpriseId);
            if (user is null)
                return Unauthorized();

            var effective = _permissionEvaluator == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : await _permissionEvaluator.EffectiveAsync(User, user);

            var feed = await _notificationService.GetFeedAsync(enterpriseId, userId, HttpContext.RequestAborted);

            feed.Items = feed.Items
                .Where(item => item.RequiredPermission is null
                               || effective.Contains(item.RequiredPermission))
                .ToList();

            feed.ActiveAlertCount = feed.Items.Count(item => item.Source == NotificationSources.Alert);
            feed.UnreadBroadcastCount = feed.Items.Count(item =>
                item.Source == NotificationSources.Broadcast && !item.IsRead);

            return Ok(feed);
        }

        [Authorize(Policy = RbacPolicies.Notifications.Read)]
        [HttpPost("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> MarkRead(Guid id)
        {
            if (!TryGetCaller(out var enterpriseId, out var userId))
                return Unauthorized();

            await _notificationService.MarkReadAsync(id, enterpriseId, userId, HttpContext.RequestAborted);

            return NoContent();
        }

        [Authorize(Policy = RbacPolicies.Notifications.Read)]
        [HttpPost]
        [ProducesResponseType(typeof(MarkAllReadResponseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> MarkAllRead()
        {
            if (!TryGetCaller(out var enterpriseId, out var userId))
                return Unauthorized();

            var marked = await _notificationService.MarkAllReadAsync(enterpriseId, userId, HttpContext.RequestAborted);

            return Ok(new MarkAllReadResponseDTO { Marked = marked });
        }

        /// <summary>Writes one message for the whole tenant. Admin only — see the permission.</summary>
        [Authorize(Policy = RbacPolicies.Notifications.Broadcast)]
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Broadcast([FromBody] CreateBroadcastDTO broadcast)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetCaller(out var enterpriseId, out var userId))
                return Unauthorized();

            var created = await _notificationService.BroadcastAsync(
                enterpriseId, userId, broadcast, HttpContext.RequestAborted);

            await PushAsync(enterpriseId);

            return Ok(new { id = created.Id });
        }

        [Authorize(Policy = RbacPolicies.Notifications.Broadcast)]
        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RemoveBroadcast(Guid id)
        {
            if (!TryGetCaller(out var enterpriseId, out _))
                return Unauthorized();

            if (!await _notificationService.RemoveBroadcastAsync(id, enterpriseId, HttpContext.RequestAborted))
                return NotFound();

            await PushAsync(enterpriseId);

            return NoContent();
        }

        /// <summary>
        /// Worker-facing. Recomputes each tenant's alert set and pushes only where it changed.
        /// </summary>
        /// <remarks>
        /// Gated by the self-API machine policy, the same gate the weekly stock and monthly billing
        /// broadcasts carry, so an interactive admin cannot drive it. It writes nothing: a tenant
        /// whose alerts are unchanged costs one panel read and one Redis get.
        ///
        /// The enterprise scan reaches every tenant because <c>AppDbContext.AmbientEnterpriseId</c>
        /// stands down for a self-API token — see the remarks there. Every read below still names
        /// its enterprise: <c>GetAlertsAsync</c> is given one id at a time, and the panel behind it
        /// filters on that id in every query.
        ///
        /// ponytail: sequential per-tenant loop on a 15-minute floor. At ~50 tenants that is
        /// seconds; parallelise or shard the lease if the sweep ever outruns its slot.
        /// </remarks>
        [Authorize(Policy = RbacPolicies.Machine.SelfApiWorker)]
        [HttpPost]
        [ProducesResponseType(typeof(NotificationSweepResultDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> SweepAlerts()
        {
            var cancellation = HttpContext.RequestAborted;
            var result = new NotificationSweepResultDTO();

            foreach (var enterprise in await ActiveEnterprisesAsync())
            {
                cancellation.ThrowIfCancellationRequested();
                result.EnterprisesScanned++;

                try
                {
                    var (_, fingerprint) = await _notificationService.GetAlertsAsync(enterprise.Id, cancellation);

                    if (fingerprint is null)
                    {
                        result.EnterprisesFailed++;
                        continue;
                    }

                    var hash = Hash(fingerprint);
                    var key = AlertHashKeyPrefix + enterprise.Id;

                    if (await _cache.GetStringAsync(key, cancellation) == hash)
                        continue;

                    await _cache.SetStringAsync(key, hash, AlertHashLifetime, cancellation);
                    await PushAsync(enterprise.Id);
                    result.EnterprisesNotified++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.EnterprisesFailed++;
                    Log.Logger.Error(
                        "Notification sweep failed for one tenant. ErrorType={ErrorType}", ex.GetType().Name);
                }
            }

            return Ok(result);
        }

        private async Task<IReadOnlyList<Enterprise>> ActiveEnterprisesAsync()
        {
            try
            {
                return (await _enterprises.GetAllAsync(enterprise => enterprise.IsActive == true)).ToList();
            }
            catch (NotFoundDatabaseException)
            {
                return [];
            }
        }

        /// <summary>Content-free by design: the client refetches the feed under its own permissions.</summary>
        private async Task PushAsync(Guid enterpriseId)
        {
            try
            {
                await _hub.Clients.Group(enterpriseId.ToString()).SendAsync(NotificationsChanged);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(
                    "Notification push failed. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }

        private static string Hash(string fingerprint) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));

        private async Task<User?> TryGetCallerUserAsync(Guid userId, Guid enterpriseId)
        {
            try
            {
                var user = await _users.GetByIdAsync(userId);

                return user is null || user.IsActive != true || user.EnterpriseId != enterpriseId
                    ? null
                    : user;
            }
            catch (NotFoundDatabaseException)
            {
                return null;
            }
        }

        private bool TryGetCaller(out Guid enterpriseId, out Guid userId)
        {
            userId = Guid.Empty;

            var tenantClaim = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            if (!Guid.TryParse(tenantClaim, out enterpriseId) || enterpriseId == Guid.Empty)
                return false;

            var userClaim = User.FindFirst(ClaimTypes.Sid)?.Value ?? User.FindFirst("uid")?.Value;

            return Guid.TryParse(userClaim, out userId) && userId != Guid.Empty;
        }
    }
}
