using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EvangelionERPV2.Shared.Hubs
{
    [Authorize]
    public class OrderHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var enterpriseClaim = Context.User?.FindFirst(ClaimTypes.GroupSid)?.Value;
            if (!string.IsNullOrWhiteSpace(enterpriseClaim) &&
                Guid.TryParse(enterpriseClaim, out var enterpriseId) &&
                enterpriseId != Guid.Empty)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, enterpriseId.ToString());
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var enterpriseClaim = Context.User?.FindFirst(ClaimTypes.GroupSid)?.Value;
            if (!string.IsNullOrWhiteSpace(enterpriseClaim) &&
                Guid.TryParse(enterpriseClaim, out var enterpriseId) &&
                enterpriseId != Guid.Empty)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, enterpriseId.ToString());
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
