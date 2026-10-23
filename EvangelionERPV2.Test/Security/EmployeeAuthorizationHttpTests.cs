using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using Asp.Versioning;
using AutoMapper;
using EvangelionERPV2.EmployeeModule.Application.Interface;
using EvangelionERPV2.EmployeeModule.Domain.Interface;
using EvangelionERPV2.Shared.DTOs;
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

public sealed class EmployeeAuthorizationHttpTests
{
    private const string EmployeesUrl = "/api/v1/Employee/GetEmployees";
    private static readonly Guid TestUserId = Guid.NewGuid();

    [Fact]
    public async Task RealEmployeeEndpoint_WithoutIdentity_ReturnsUnauthorized()
    {
        var tenantId = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Manager, tenantId), tenantId);
        using var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(EmployeesUrl)).StatusCode);
    }

    [Fact]
    public async Task RealEmployeeEndpoint_WhenTenantClaimDiffersFromUserTenant_ReturnsUnauthorized()
    {
        var storedTenant = Guid.NewGuid();
        var claimedTenant = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Manager, storedTenant), claimedTenant);
        using var client = host.GetTestClient();
        AddIdentity(client, claimedTenant);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(EmployeesUrl)).StatusCode);
    }

    [Fact]
    public async Task RealEmployeeEndpoint_WhenRoleLacksEmployeesRead_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Employee, tenantId), tenantId);
        using var client = host.GetTestClient();
        AddIdentity(client, tenantId);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(EmployeesUrl)).StatusCode);
    }

    [Fact]
    public async Task RealEmployeeEndpoint_WithValidTenantAndRole_ReachesController()
    {
        var tenantId = Guid.NewGuid();
        using var host = await CreateHostAsync(ActiveUser(EnumAccessLevel.Manager, tenantId), tenantId);
        using var client = host.GetTestClient();
        AddIdentity(client, tenantId);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(EmployeesUrl)).StatusCode);
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
        var employees = new Mock<IRepository<Employee>>();
        employees.Setup(repository => repository.GetAllAsync(
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Employee, bool>>>() ))
            .ReturnsAsync(Array.Empty<Employee>());

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
                    services.AddControllers().AddApplicationPart(typeof(EmployeeController).Assembly);
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
                    services.AddSingleton(Mock.Of<IEmployeeService<Employee>>());
                    services.AddSingleton(employees.Object);
                    services.AddSingleton(Mock.Of<IEmployeeRepository<Employee>>());
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
