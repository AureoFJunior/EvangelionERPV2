using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EvangelionERPV2.Shared.Security;
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

namespace EvangelionERPV2.Test.Security;

public sealed class SelfApiEndpointEnforcementTests
{
    [Fact]
    public async Task SelfApiToken_CanOnlyReachAnExplicitWorkerEndpoint()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Machine", "true");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/ordinary" )).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/worker" )).StatusCode);
    }

    [Fact]
    public async Task InteractiveToken_CannotReachWorkerEndpoint()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ordinary" )).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/worker" )).StatusCode);
    }

    private static async Task<IHost> CreateHostAsync() => await new HostBuilder()
        .ConfigureWebHost(webHost => webHost
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.AddAuthorization(options =>
                {
                    options.AddRbacPolicies();
                    options.DefaultPolicy = new AuthorizationPolicyBuilder("Test")
                        .RequireAuthenticatedUser()
                        .Build();
                });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseMiddleware<SelfApiEndpointEnforcementMiddleware>();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/ordinary", () => Results.Ok()).RequireAuthorization();
                    endpoints.MapGet("/worker", () => Results.Ok())
                        .RequireAuthorization(RbacPolicies.Machine.SelfApiWorker);
                });
            }))
        .StartAsync();

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "test-user") };
            if (Request.Headers["X-Test-Machine"] == "true")
                claims.Add(new Claim(MachineClaims.ClientType, MachineClaims.SelfApiValue));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}
