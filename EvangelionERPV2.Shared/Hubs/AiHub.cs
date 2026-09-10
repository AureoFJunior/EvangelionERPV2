using System.Security.Claims;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EvangelionERPV2.Shared.Hubs
{
    /// <summary>
    /// Groups by user, not by enterprise. OrderHub groups by enterprise because an order is news
    /// for the whole shop; a chat answer is a private reply to one person and quotes whatever they
    /// asked about, so broadcasting it to the tenant would leak it to every colleague online.
    /// </summary>
    [Authorize(Policy = "rbac:ai.chat")]
    public class AiHub : Hub
    {
        private readonly IRepository<User> _userRepository;
        private readonly IRepository<Enterprise> _enterpriseRepository;

        public AiHub(IRepository<User> userRepository, IRepository<Enterprise> enterpriseRepository)
        {
            _userRepository = userRepository;
            _enterpriseRepository = enterpriseRepository;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = TryGetUserId();
            var enterpriseId = TryGetEnterpriseId();
            if (!userId.HasValue || !enterpriseId.HasValue
                || !await HasActiveTenantMembershipAsync(userId.Value, enterpriseId.Value))
            {
                Context.Abort();
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, userId.Value.ToString());
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = TryGetUserId();
            if (userId.HasValue)
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId.Value.ToString());

            await base.OnDisconnectedAsync(exception);
        }

        private Guid? TryGetUserId()
        {
            var claimValue = Context.User?.FindFirst(ClaimTypes.Sid)?.Value
                             ?? Context.User?.FindFirst("uid")?.Value;

            if (Guid.TryParse(claimValue, out var userId) && userId != Guid.Empty)
                return userId;

            return null;
        }

        private Guid? TryGetEnterpriseId()
        {
            var claimValue = Context.User?.FindFirst(ClaimTypes.GroupSid)?.Value;
            if (Guid.TryParse(claimValue, out var enterpriseId) && enterpriseId != Guid.Empty)
                return enterpriseId;

            return null;
        }

        private async Task<bool> HasActiveTenantMembershipAsync(Guid userId, Guid enterpriseId)
        {
            if (userId == Guid.Empty || enterpriseId == Guid.Empty)
                return false;

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || user.IsActive != true || user.EnterpriseId != enterpriseId)
                return false;

            var enterprise = await _enterpriseRepository.GetByIdAsync(enterpriseId);
            return enterprise != null && enterprise.IsActive == true;
        }
    }
}
