using System.Net;
using System.Net.Http.Headers;
using Asp.Versioning;
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

public sealed class ProtectedControllerAuthorizationMatrixHttpTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    // One representative user-facing RBAC endpoint for each family without a dedicated HTTP test.
    private static readonly (HttpMethod Method, string Path)[] UncoveredFamilies =
    [
        (HttpMethod.Get, "/api/v1/ai/conversations"),
        (HttpMethod.Get, "/api/v1/AuditTrail/GetAuditTrails"),
        (HttpMethod.Get, "/api/v1/BankReconciliation/GetUnclearedLines"),
        (HttpMethod.Get, "/api/v1/Bills/Aging"),
        (HttpMethod.Post, "/api/v1/Email/SendManualEmail"),
        (HttpMethod.Get, "/api/v1/EmployeeAbsence/GetEmployeeAbsences"),
        (HttpMethod.Get, "/api/v1/EmployeeVacation/GetVacationAccruals"),
        (HttpMethod.Get, "/api/v1/Enterprise/GetEnterprises"),
        (HttpMethod.Get, "/api/v1/Iam/Catalog"),
        (HttpMethod.Post, "/api/v1/Messaging/RedriveDeadLettered?queue=order"),
        (HttpMethod.Get, "/api/v1/NFeFiscalSettings/GetFiscalSettings"),
        (HttpMethod.Get, "/api/v1/Notification/Feed"),
        (HttpMethod.Get, "/api/v1/opportunities"),
        (HttpMethod.Get, "/api/v1/Payroll/GetPayroll"),
        (HttpMethod.Get, "/api/v1/Pdv/GetSupervisorVerifiers"),
        (HttpMethod.Get, "/api/v1/Product/GetProducts"),
        (HttpMethod.Get, "/api/v1/Quote/GetQuotes"),
        (HttpMethod.Get, "/api/v1/reports"),
        (HttpMethod.Get, "/api/v1/SalesPerformance/GetPerformance?year=2026&month=10"),
        (HttpMethod.Get, "/api/v1/Storefront/Enterprises/1/10"),
        (HttpMethod.Get, "/api/v1/Supplier/GetSuppliers"),
        (HttpMethod.Get, "/api/v1/User/GetUsers")
    ];

    [Fact]
    public async Task EveryRemainingControllerFamily_EnforcesJwtTenantAndRbacOnItsRealEndpoint()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();
        var token = TestJwtFactory.Create(UserId, TenantId);

        foreach (var (method, path) in UncoveredFamilies)
        {
            using var anonymousRequest = new HttpRequestMessage(method, path);
            using var anonymousResponse = await client.SendAsync(anonymousRequest);
            Assert.True(anonymousResponse.StatusCode == HttpStatusCode.Unauthorized,
                $"{method} {path}: expected 401 without JWT, received {(int)anonymousResponse.StatusCode}.");

            using var forbiddenRequest = new HttpRequestMessage(method, path);
            forbiddenRequest.Headers.Authorization = new AuthenticationHeaderValue(
                JwtBearerDefaults.AuthenticationScheme, token);
            if (method == HttpMethod.Post)
                forbiddenRequest.Content = new StringContent(string.Empty);
            using var forbiddenResponse = await client.SendAsync(forbiddenRequest);
            Assert.True(forbiddenResponse.StatusCode == HttpStatusCode.Forbidden,
                $"{method} {path}: expected 403 for active tenant with no role permissions, received {(int)forbiddenResponse.StatusCode}.");
        }
    }

    private static async Task<IHost> CreateHostAsync()
    {
        var users = new Mock<IRepository<User>>();
        users.Setup(repository => repository.GetByIdAsync(UserId)).ReturnsAsync(new User
        {
            Id = UserId,
            EnterpriseId = TenantId,
            IsActive = true,
            AccessLevel = (short)99
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
                    services.AddApiVersioning(options =>
                    {
                        options.DefaultApiVersion = new ApiVersion(1, 0);
                        options.AssumeDefaultVersionWhenUnspecified = true;
                    }).AddMvc();
                    services.AddControllers().AddApplicationPart(typeof(AiController).Assembly);
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
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .StartAsync();
    }
}
