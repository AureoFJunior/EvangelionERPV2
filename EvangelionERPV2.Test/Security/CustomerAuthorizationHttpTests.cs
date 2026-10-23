using System.Net;
using System.Net.Http.Headers;
using Asp.Versioning;
using AutoMapper;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
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

public sealed class CustomerAuthorizationHttpTests
{
    private const string NearbyCustomersUrl = "/api/v1/Customer/GetNearbyCustomers?latitude=-30&longitude=-51";
    private static readonly Guid TestUserId = Guid.NewGuid();

    [Fact]
    public async Task RealCustomerEndpoint_WithoutIdentity_ReturnsUnauthorized()
    {
        var tenantId = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Supervisor, tenantId), tenantId);
        using var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(NearbyCustomersUrl)).StatusCode);
    }

    [Fact]
    public async Task RealCustomerEndpoint_WhenTenantClaimDiffersFromUserTenant_ReturnsUnauthorized()
    {
        var storedTenant = Guid.NewGuid();
        var claimedTenant = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Supervisor, storedTenant), claimedTenant);
        using var client = host.GetTestClient();
        AddIdentity(client, claimedTenant);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(NearbyCustomersUrl)).StatusCode);
    }

    [Fact]
    public async Task RealCustomerEndpoint_WhenRoleLacksCustomersRead_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser((EnumAccessLevel)99, tenantId), tenantId);
        using var client = host.GetTestClient();
        AddIdentity(client, tenantId);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(NearbyCustomersUrl)).StatusCode);
    }

    [Fact]
    public async Task RealCustomerEndpoint_WithValidTenantAndRole_ReachesController()
    {
        var tenantId = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Supervisor, tenantId), tenantId);
        using var client = host.GetTestClient();
        AddIdentity(client, tenantId);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(NearbyCustomersUrl)).StatusCode);
    }

    private static User ActiveUser(EnumAccessLevel role, Guid tenantId) => new()
    {
        Id = TestUserId,
        EnterpriseId = tenantId,
        IsActive = true,
        AccessLevel = (short)role
    };

    private static void AddIdentity(HttpClient client, Guid tenantId) =>
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
        var locations = new Mock<ICustomerLocationService>();
        locations.Setup(service => service.GetNearbyAsync(
                claimTenantId, -30m, -51m, 50, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

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
                    services.AddControllers().AddApplicationPart(typeof(CustomerController).Assembly);
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
                    services.AddSingleton(Mock.Of<ICustomerService<Customer>>());
                    services.AddSingleton(Mock.Of<IRepository<Customer>>());
                    services.AddSingleton(Mock.Of<ICustomerSummaryService>());
                    services.AddSingleton(locations.Object);
                    services.AddSingleton(Mock.Of<IMapper>());
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
