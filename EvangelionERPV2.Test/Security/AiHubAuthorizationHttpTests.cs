using System.Net;
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

public sealed class AiHubAuthorizationHttpTests
{
    [Fact]
    public async Task HubNegotiation_WithoutIdentity_ReturnsUnauthorized()
    {
        var tenantId = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Supervisor, tenantId), tenantId);
        using var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await NegotiateAsync(client)).StatusCode);
    }

    [Fact]
    public async Task HubNegotiation_WhenTenantClaimDoesNotMatchUser_ReturnsUnauthorized()
    {
        var storedTenant = Guid.NewGuid();
        var claimedTenant = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Supervisor, storedTenant), claimedTenant);
        using var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await NegotiateAsync(client, TestJwtFactory.Create(TestUserId, claimedTenant))).StatusCode);
    }

    [Fact]
    public async Task HubNegotiation_WhenRoleLacksAiChat_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser((EnumAccessLevel)99, tenantId), tenantId);
        using var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await NegotiateAsync(client, TestJwtFactory.Create(TestUserId, tenantId))).StatusCode);
    }

    [Fact]
    public async Task HubNegotiation_WithValidJwtTenantAndRoleInQueryString_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Supervisor, tenantId), tenantId);
        using var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.OK,
            (await NegotiateAsync(client, TestJwtFactory.Create(TestUserId, tenantId))).StatusCode);
    }

    private static Task<HttpResponseMessage> NegotiateAsync(HttpClient client, string? token = null)
    {
        var accessToken = token is null ? string.Empty : $"&access_token={Uri.EscapeDataString(token)}";
        return client.PostAsync($"/aiHub/negotiate?negotiateVersion=1{accessToken}", new StringContent(string.Empty));
    }

    private static User ActiveUser(EnumAccessLevel role, Guid tenantId) => new()
    {
        Id = TestUserId,
        EnterpriseId = tenantId,
        IsActive = true,
        AccessLevel = (short)role
    };

    private static readonly Guid TestUserId = Guid.NewGuid();

    private static async Task<IHost> CreateHostAsync(User user, Guid claimTenantId)
    {
        var users = new Mock<IRepository<User>>();
        users.Setup(repository => repository.GetByIdAsync(user.Id)).ReturnsAsync(user);
        var enterprises = new Mock<IRepository<Enterprise>>();
        enterprises.Setup(repository => repository.GetByIdAsync(claimTenantId))
            .ReturnsAsync(new Enterprise { Id = claimTenantId, IsActive = true });

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
                                    if (!string.IsNullOrEmpty(accessToken)
                                        && context.HttpContext.Request.Path.StartsWithSegments("/aiHub"))
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
                    app.UseMiddleware<SelfApiEndpointEnforcementMiddleware>();
                    app.UseMiddleware<ActiveTenantEnforcementMiddleware>();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapHub<AiHub>("/aiHub"));
                }))
            .StartAsync();
    }
}
