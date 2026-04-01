using EvangelionERPV2.Web.Logging;
using Microsoft.AspNetCore.Http;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class RequestLoggingMiddlewareEndpointSanitizationTests
    {
        private static readonly MethodInfo SanitizeEndpointPathMethod =
            typeof(RequestLoggingMiddleware).GetMethod("SanitizeEndpointPath", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SanitizeEndpointPath method was not found.");

        [Fact]
        public void SanitizeEndpointPath_NFeConsultWithAccessKey_RedactsAccessKeySegment()
        {
            var sanitized = InvokeSanitizeEndpointPath("/api/v1/NFe/Consult/35240112345678901234550010000000011000000010");

            Assert.Equal("/api/v1/NFe/Consult/[redacted]", sanitized);
        }

        [Fact]
        public void SanitizeEndpointPath_NFeCancelWithAccessKey_RedactsAccessKeySegment()
        {
            var sanitized = InvokeSanitizeEndpointPath("/api/v1/NFe/Cancel/35240112345678901234550010000000011000000010");

            Assert.Equal("/api/v1/NFe/Cancel/[redacted]", sanitized);
        }

        [Fact]
        public void SanitizeEndpointPath_NonSensitivePath_RemainsUnchanged()
        {
            var sanitized = InvokeSanitizeEndpointPath("/api/v1/Product/GetProducts");

            Assert.Equal("/api/v1/Product/GetProducts", sanitized);
        }

        private static string InvokeSanitizeEndpointPath(string path)
        {
            var result = SanitizeEndpointPathMethod.Invoke(null, [new PathString(path)]);
            return Assert.IsType<string>(result);
        }
    }
}
