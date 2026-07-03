using EvangelionERPV2.Web.Logging;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;

namespace EvangelionERPV2.Test.Security
{
    public class RequestLoggingMiddlewareResolveCallerTests
    {
        private static readonly MethodInfo ResolveCallerMethod =
            typeof(RequestLoggingMiddleware).GetMethod("ResolveCaller", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ResolveCaller method was not found.");

        [Fact]
        public void ResolveCaller_WithGuidSidClaim_ReturnsGuidValue()
        {
            var callerId = Guid.NewGuid().ToString();
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.Sid, callerId),
                        new Claim(ClaimTypes.NameIdentifier, "user@example.com")
                    ],
                    authenticationType: "UnitTestAuth"))
            };

            var caller = InvokeResolveCaller(context);

            Assert.Equal(callerId, caller);
        }

        [Fact]
        public void ResolveCaller_WithoutGuidClaims_ReturnsAuthenticatedMarker()
        {
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "user@example.com"),
                        new Claim(ClaimTypes.Email, "user@example.com")
                    ],
                    authenticationType: "UnitTestAuth"))
            };

            var caller = InvokeResolveCaller(context);

            Assert.Equal("authenticated", caller);
        }

        [Fact]
        public void ResolveCaller_WhenAnonymous_ReturnsStableIpFingerprint()
        {
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            };
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.10.10.10");

            var caller = InvokeResolveCaller(context);

            Assert.Equal($"anonymous:{CreateStableFingerprint("10.10.10.10")}", caller);
        }

        [Fact]
        public void ResolveCaller_WhenAnonymousWithoutIp_ReturnsAnonymousMarker()
        {
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            };

            var caller = InvokeResolveCaller(context);

            Assert.Equal("anonymous", caller);
        }

        private static string InvokeResolveCaller(HttpContext context)
        {
            return (string)ResolveCallerMethod.Invoke(null, [context])!;
        }

        private static string CreateStableFingerprint(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
            return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
        }
    }
}
