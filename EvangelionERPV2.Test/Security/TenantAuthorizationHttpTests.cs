using System.Security.Claims;
using System.Text.Encodings.Web;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EvangelionERPV2.Test.Security;

public sealed class TenantAuthorizationHttpTests
{
    [Fact]
    public async Task GetProtectedResource_WhenClaimTenantDiffersFromPersistedTenant_ReturnsUnauthorized()
    {
        var userId = Guid.NewGuid();
        var claimTenantId = Guid.NewGuid();
        var userRepository = UserRepository(new User
        {
            Id = userId,
            EnterpriseId = Guid.NewGuid(),
            IsActive = true,
            AccessLevel = (short)EnumAccessLevel.Admin
        }, userId);

        using var host = await CreateHostAsync(userRepository, claimTenantId);
        using var client = host.GetTestClient();
        AddTestIdentity(client, userId, claimTenantId);

        var response = await client.GetAsync("/protected");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProtectedResource_WhenRoleLacksPermission_ReturnsForbidden()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userRepository = UserRepository(new User
        {
            Id = userId,
            EnterpriseId = tenantId,
            IsActive = true,
            AccessLevel = (short)EnumAccessLevel.Employee
        }, userId);

        using var host = await CreateHostAsync(userRepository, tenantId);
        using var client = host.GetTestClient();
        AddTestIdentity(client, userId, tenantId);

        var response = await client.GetAsync("/protected");

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetProtectedResource_WhenTenantAndRoleAreAuthorized_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userRepository = UserRepository(new User
        {
            Id = userId,
            EnterpriseId = tenantId,
            IsActive = true,
            AccessLevel = (short)EnumAccessLevel.Supervisor
        }, userId);

        using var host = await CreateHostAsync(userRepository, tenantId);
        using var client = host.GetTestClient();
        AddTestIdentity(client, userId, tenantId);

        var response = await client.GetAsync("/protected");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<IHost> CreateHostAsync(Mock<IRepository<User>> userRepository, Guid claimTenantId)
    {
        var enterpriseRepository = new Mock<IRepository<Enterprise>>(MockBehavior.Strict);
        enterpriseRepository
            .Setup(repository => repository.GetByIdAsync(claimTenantId))
            .ReturnsAsync(new Enterprise { Id = claimTenantId, IsActive = true });

        return await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                    services.AddSingleton<IRepository<User>>(userRepository.Object);
                    services.AddSingleton<IRepository<Enterprise>>(enterpriseRepository.Object);
                    services.AddSingleton<IPermissionEvaluator, RolePermissionEvaluator>();
                    services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
                    services.AddAuthorization(options => options.AddPolicy("test:orders.update", policy =>
                    {
                        policy.RequireAuthenticatedUser();
                        policy.AddRequirements(new PermissionRequirement(RbacPermissions.Orders.Update));
                    }));
                })
                .Configure(application =>
                {
                    application.UseRouting();
                    application.UseAuthentication();
                    application.UseMiddleware<ActiveTenantEnforcementMiddleware>();
                    application.UseAuthorization();
                    application.UseEndpoints(endpoints => endpoints
                        .MapGet("/protected", () => Results.Ok())
                        .RequireAuthorization("test:orders.update"));
                }))
            .StartAsync();
    }

    private static void AddTestIdentity(HttpClient client, Guid userId, Guid tenantId)
    {
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Tenant", tenantId.ToString());
    }

    private static Mock<IRepository<User>> UserRepository(User user, Guid userId)
    {
        var repository = new Mock<IRepository<User>>(MockBehavior.Strict);
        repository
            .Setup(service => service.GetByIdAsync(userId))
            .ReturnsAsync(user);
        return repository;
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Test-User"], out var userId) ||
                !Guid.TryParse(Request.Headers["X-Test-Tenant"], out var tenantId))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Sid, userId.ToString()),
                new Claim(ClaimTypes.GroupSid, tenantId.ToString())
            ], Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
