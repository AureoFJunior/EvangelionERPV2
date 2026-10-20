using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace EvangelionERPV2.Test.Security;

internal static class TestJwtFactory
{
    internal const string Issuer = "evangelion-test-issuer";
    internal const string Audience = "evangelion-test-audience";
    private const string SigningKey = "test-only-signing-key-32-bytes-long";

    internal static TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = ClaimTypes.NameIdentifier,
        RoleClaimType = ClaimTypes.Role
    };

    internal static string Create(Guid userId, Guid tenantId)
    {
        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            [
                new Claim(ClaimTypes.Sid, userId.ToString()),
                new Claim(ClaimTypes.GroupSid, tenantId.ToString())
            ],
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
