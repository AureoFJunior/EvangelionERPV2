using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Hubs;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace EvangelionERPV2.Test.Security;

public sealed class SignalRWebSocketHandshakeHttpTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    [Theory]
    [InlineData("/aiHub", "aiHub")]
    [InlineData("/orderHub", "orderHub")]
    public async Task Hub_WithValidQueryJwt_CompletesSignalRWebSocketHandshake(string path, string hubName)
    {
        using var host = await CreateHostAsync();
        using var http = host.GetTestClient();
        var token = TestJwtFactory.Create(UserId, TenantId);
        var negotiation = await http.PostAsync(
            $"{path}/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(token)}",
            new StringContent(string.Empty));

        Assert.Equal(System.Net.HttpStatusCode.OK, negotiation.StatusCode);
        using var negotiationJson = JsonDocument.Parse(await negotiation.Content.ReadAsStringAsync());
        var connectionToken = negotiationJson.RootElement.GetProperty("connectionToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(connectionToken));

        var webSocketClient = host.GetTestServer().CreateWebSocketClient();
        var socketUri = new Uri(
            $"ws://localhost/{hubName}?id={Uri.EscapeDataString(connectionToken!)}&access_token={Uri.EscapeDataString(token)}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await webSocketClient.ConnectAsync(socketUri, timeout.Token);
        var handshake = Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e");
        await socket.SendAsync(handshake, WebSocketMessageType.Text, true, timeout.Token);

        var response = new byte[128];
        var result = await socket.ReceiveAsync(response, timeout.Token);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.Equal("{}\u001e", Encoding.UTF8.GetString(response, 0, result.Count));
    }

    private static async Task<IHost> CreateHostAsync()
    {
        var users = new Mock<IRepository<User>>();
        users.Setup(repository => repository.GetByIdAsync(UserId)).ReturnsAsync(new User
        {
            Id = UserId,
            EnterpriseId = TenantId,
            IsActive = true,
            AccessLevel = (short)EnumAccessLevel.Supervisor
        });
        var enterprises = new Mock<IRepository<Enterprise>>();
        enterprises.Setup(repository => repository.GetByIdAsync(TenantId))
            .ReturnsAsync(new Enterprise { Id = TenantId, IsActive = true });

        return await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSignalR();
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                        .AddJwtBearer(options =>
                        {
                            options.MapInboundClaims = false;
                            options.TokenValidationParameters = TestJwtFactory.ValidationParameters();
                            options.Events = new JwtBearerEvents
                            {
                                OnMessageReceived = context =>
                                {
                                    var accessToken = context.Request.Query["access_token"].FirstOrDefault();
                                    var requestPath = context.HttpContext.Request.Path;
                                    if (!string.IsNullOrEmpty(accessToken)
                                        && (requestPath.StartsWithSegments("/aiHub")
                                            || requestPath.StartsWithSegments("/orderHub")))
                                        context.Token = accessToken;
                                    return Task.CompletedTask;
                                }
                            };
                        });
                    services.AddAuthorization(options =>
                    {
                        options.FallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                            .RequireAuthenticatedUser()
                            .Build();
                        options.AddRbacPolicies();
                    });
                    services.AddSingleton(users.Object);
                    services.AddSingleton(enterprises.Object);
                    services.AddSingleton<IPermissionEvaluator, RolePermissionEvaluator>();
                    services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseMiddleware<ActiveTenantEnforcementMiddleware>();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHub<AiHub>("/aiHub").RequireAuthorization(RbacPolicies.Ai.Chat);
                        endpoints.MapHub<OrderHub>("/orderHub").RequireAuthorization(RbacPolicies.Orders.Read);
                    });
                }))
            .StartAsync();
    }
}
