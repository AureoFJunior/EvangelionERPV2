using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace EvangelionERPV2.Test.Security;

public sealed class ProductionJwtConfigurationTests
{
    private const string SigningKey = "production-config-test-signing-key-32-bytes";

    [Fact]
    public async Task ProductionConfiguration_ResolvesSigningKeyAndValidatesIssuerAudienceAndHubQueryToken()
    {
        var configurationPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../EvangelionERPV2.Web/appsettings.Production.json"));
        Assert.True(File.Exists(configurationPath), $"Production config not found at {configurationPath}.");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            ContentRootPath = Path.GetDirectoryName(configurationPath)!
        });
        builder.Configuration.AddJsonFile(configurationPath, optional: false, reloadOnChange: false);

        var secretName = builder.Configuration["Encryption:TokenKey"]!;
        AWSKMSKeyProvider.InvalidateCachedSecret(secretName);
        var secretId = secretName[..secretName.LastIndexOf(':')];
        var secrets = new Mock<IAmazonSecretsManager>(MockBehavior.Strict);
        secrets.Setup(client => client.GetSecretValueAsync(
                It.Is<GetSecretValueRequest>(request => request.SecretId == secretId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = $$"""{"key":"{{SigningKey}}"}""" });
        builder.Services.AddSingleton(secrets.Object);
        builder.Services.AddSingleton<AWSKMSKeyProvider>();

        SetupJwtMethod().Invoke(null, [builder]);

        using var services = builder.Services.BuildServiceProvider();
        var options = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        AWSKMSKeyProvider.InvalidateCachedSecret(secretName);
        var parameters = options.TokenValidationParameters;
        Assert.Equal("EvangelionERP", parameters.ValidIssuer);
        Assert.Equal("EvangelionERP", parameters.ValidAudience);
        Assert.True(parameters.ValidateIssuerSigningKey);
        Assert.True(options.RequireHttpsMetadata);
        Assert.Equal(Encoding.UTF8.GetBytes(SigningKey),
            Assert.IsType<SymmetricSecurityKey>(parameters.IssuerSigningKey).Key);

        var now = DateTime.UtcNow;
        var signed = new JwtSecurityToken(
            parameters.ValidIssuer,
            parameters.ValidAudience,
            [new Claim(ClaimTypes.Sid, Guid.NewGuid().ToString())],
            now.AddMinutes(-1),
            now.AddMinutes(5),
            new SigningCredentials(parameters.IssuerSigningKey, SecurityAlgorithms.HmacSha256));
        var token = new JwtSecurityTokenHandler().WriteToken(signed);
        new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);

        Assert.Equal(token, await ReadHubQueryTokenAsync(options, "/aiHub", token));
        Assert.Equal(token, await ReadHubQueryTokenAsync(options, "/orderHub", token));
        Assert.Null(await ReadHubQueryTokenAsync(options, "/api/v1/orders", token));
    }

    [Theory]
    [InlineData("JwtSettings:Issuer")]
    [InlineData("JwtSettings:Audience")]
    public void SetupJwt_RejectsMissingIssuerOrAudience(string missingSetting)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Issuer"] = missingSetting == "JwtSettings:Issuer" ? "" : "test-issuer",
            ["JwtSettings:Audience"] = missingSetting == "JwtSettings:Audience" ? "" : "test-audience"
        });

        var exception = Assert.Throws<TargetInvocationException>(() => SetupJwtMethod().Invoke(null, [builder]));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    private static MethodInfo SetupJwtMethod() =>
        typeof(OrderController).Assembly.GetType("Program", throwOnError: true)!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.Contains("SetupJWT", StringComparison.Ordinal));

    private static async Task<string?> ReadHubQueryTokenAsync(JwtBearerOptions options, string path, string token)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        httpContext.Request.QueryString = new QueryString($"?access_token={Uri.EscapeDataString(token)}");
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme,
            JwtBearerDefaults.AuthenticationScheme,
            typeof(JwtBearerHandler));
        var message = new MessageReceivedContext(httpContext, scheme, options);

        await options.Events.OnMessageReceived(message);
        return message.Token;
    }
}
