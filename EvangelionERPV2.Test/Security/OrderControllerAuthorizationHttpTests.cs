using System.Net;
using System.Net.Http.Headers;
using Asp.Versioning;
using AutoMapper;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace EvangelionERPV2.Test.Security;

public sealed class OrderControllerAuthorizationHttpTests
{
    private static readonly string OrdersUrl = "/api/v1/Order/GetOrders";

    [Fact]
    public async Task RealOrderEndpoint_WithoutIdentity_ReturnsUnauthorized()
    {
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Supervisor));
        using var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(OrdersUrl)).StatusCode);
    }

    [Fact]
    public async Task RealOrderEndpoint_WithInvalidJwt_ReturnsUnauthorized()
    {
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Supervisor));
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.valid.jwt");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(OrdersUrl)).StatusCode);
    }

    [Fact]
    public async Task RealOrderEndpoint_WhenClaimTenantDiffersFromUserTenant_ReturnsUnauthorized()
    {
        var storedTenant = Guid.NewGuid();
        var claimedTenant = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Supervisor, storedTenant), claimedTenant);
        using var client = host.GetTestClient();
        AddIdentity(client, claimedTenant);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(OrdersUrl)).StatusCode);
    }

    [Fact]
    public async Task RealOrderEndpoint_WhenRoleLacksPermission_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser((EnumAccessLevel)99, tenantId), tenantId);
        using var client = host.GetTestClient();
        AddIdentity(client, tenantId);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(OrdersUrl)).StatusCode);
    }

    [Fact]
    public async Task RealOrderEndpoint_WhenTenantAndRoleAreValid_ReachesController()
    {
        var tenantId = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Supervisor, tenantId), tenantId);
        using var client = host.GetTestClient();
        AddIdentity(client, tenantId);

        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync(OrdersUrl)).StatusCode);
    }

    private static User ActiveUser(EnumAccessLevel role, Guid? tenantId = null) => new()
    {
        Id = TestUserId,
        EnterpriseId = tenantId ?? Guid.NewGuid(),
        IsActive = true,
        AccessLevel = (short)role
    };

    private static void AddIdentity(HttpClient client, Guid tenantId)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            TestJwtFactory.Create(TestUserId, tenantId));
    }

    private static readonly Guid TestUserId = Guid.NewGuid();

    private static async Task<IHost> CreateHostAsync(User user, Guid? claimedTenantId = null)
    {
        var users = new Mock<IRepository<User>>();
        users.Setup(repository => repository.GetByIdAsync(user.Id)).ReturnsAsync(user);
        var enterprises = new Mock<IRepository<Enterprise>>();
        enterprises.Setup(repository => repository.GetByIdAsync(claimedTenantId ?? user.EnterpriseId!.Value))
            .ReturnsAsync((Guid id) => new Enterprise { Id = id, IsActive = true });

        var orders = new Mock<IOrderService<Order>>();
        orders.Setup(service => service.GetByEnterpriseIdAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Array.Empty<Order>());

        return await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddApiVersioning(options =>
                    {
                        options.DefaultApiVersion = new ApiVersion(1, 0);
                        options.AssumeDefaultVersionWhenUnspecified = true;
                    }).AddMvc();
                    services.AddControllers().AddApplicationPart(typeof(OrderController).Assembly);
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
                    services.AddSingleton(orders.Object);
                    services.AddSingleton(Mock.Of<IMapper>());
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
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .StartAsync();
    }

}
