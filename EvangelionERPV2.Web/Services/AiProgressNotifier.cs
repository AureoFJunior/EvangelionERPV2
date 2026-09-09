using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace EvangelionERPV2.Web.Services
{
    public sealed class AiProgressNotifier : IAiProgressNotifier
    {
        private readonly IHubContext<AiHub> _hub;

        public AiProgressNotifier(IHubContext<AiHub> hub) => _hub = hub;

        public Task StatusAsync(Guid userId, string status) =>
            _hub.Clients.Group(userId.ToString()).SendAsync("AiStatus", status);

        public Task ChunkAsync(Guid userId, string text) =>
            _hub.Clients.Group(userId.ToString()).SendAsync("AiChunk", text);

        public Task DoneAsync(Guid userId, Guid conversationId) =>
            _hub.Clients.Group(userId.ToString()).SendAsync("AiDone", conversationId);
    }
}
