using System.Net;
using System.Net.Http.Headers;
using Asp.Versioning;
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

public sealed class OrderHubAuthorizationHttpTests
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
        AddBearer(client, claimedTenant);

        Assert.Equal(HttpStatusCode.Unauthorized, (await NegotiateAsync(client)).StatusCode);
    }

    [Fact]
    public async Task HubNegotiation_WhenRoleLacksOrdersRead_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser((EnumAccessLevel)99, tenantId), tenantId);
        using var client = host.GetTestClient();
        AddBearer(client, tenantId);

        Assert.Equal(HttpStatusCode.Forbidden, (await NegotiateAsync(client)).StatusCode);
    }

    [Fact]
    public async Task HubNegotiation_WithValidJwtTenantAndRole_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Employee, tenantId), tenantId);
        using var client = host.GetTestClient();
        AddBearer(client, tenantId);

        Assert.Equal(HttpStatusCode.OK, (await NegotiateAsync(client)).StatusCode);
    }

    private static Task<HttpResponseMessage> NegotiateAsync(HttpClient client) =>
        client.PostAsync("/orderHub/negotiate?negotiateVersion=1", new StringContent(string.Empty));

    private static User ActiveUser(EnumAccessLevel role, Guid tenantId) => new()
    {
        Id = TestUserId,
        EnterpriseId = tenantId,
        IsActive = true,
        AccessLevel = (short)role
    };

    private static readonly Guid TestUserId = Guid.NewGuid();

    private static void AddBearer(HttpClient client, Guid tenantId) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            TestJwtFactory.Create(TestUserId, tenantId));

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
                    app.UseEndpoints(endpoints => endpoints.MapHub<OrderHub>("/orderHub"));
                }))
            .StartAsync();
    }
}
