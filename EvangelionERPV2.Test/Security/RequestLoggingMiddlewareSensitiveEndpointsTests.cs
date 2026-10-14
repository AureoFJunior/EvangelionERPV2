using EvangelionERPV2.Web.Logging;
using Microsoft.AspNetCore.Http;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class RequestLoggingMiddlewareSensitiveEndpointsTests
    {
        private static readonly MethodInfo IsSensitiveEndpointMethod =
            typeof(RequestLoggingMiddleware).GetMethod("IsSensitiveEndpoint", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("IsSensitiveEndpoint method was not found.");

        [Theory]
        [InlineData("/api/v1/User/LogInto")]
        [InlineData("/api/v1/User/LoginWithGoogle")]
        [InlineData("/api/v1/User/LoginWithGoogleCode")]
        [InlineData("/api/v1/User/AddUser")]
        [InlineData("/api/v1/User/UpdateUser")]
        [InlineData("/api/v1/Email/AddEmail")]
        [InlineData("/api/v1/Email/SendEmail")]
        [InlineData("/api/v1/Email/SendManualEmail")]
        [InlineData("/api/v1/Customer/AddCustomer")]
        [InlineData("/api/v1/Customer/UpdateCustomer")]
        [InlineData("/api/v1/Enterprise/AddEnterprise")]
        [InlineData("/api/v1/Enterprise/UpdateEnterprise")]
        [InlineData("/api/v1/Order/AddOrder")]
        [InlineData("/api/v1/Order/InsertOrder")]
        [InlineData("/api/v1/Order/UpdateOrder")]
        [InlineData("/api/v1/Order/RefundOrder")]
        [InlineData("/api/v1/PayableBills/AddPayableBill")]
        [InlineData("/api/v1/PayableBills/UpdatePayableBill")]
        [InlineData("/api/v1/PayableBills/RefundPayableBill")]
        [InlineData("/api/v1/User/RequestPasswordReset")]
        [InlineData("/api/v1/User/ResetPassword")]
        public void IsSensitiveEndpoint_KnownSensitivePaths_ReturnsTrue(string path)
        {
            var isSensitive = InvokeIsSensitiveEndpoint(path);

            Assert.True(isSensitive);
        }

        [Fact]
        public void IsSensitiveEndpoint_NonSensitivePath_ReturnsFalse()
        {
            var isSensitive = InvokeIsSensitiveEndpoint("/api/v1/Product/GetProducts");

            Assert.False(isSensitive);
        }

        private static bool InvokeIsSensitiveEndpoint(string path)
        {
            return (bool)IsSensitiveEndpointMethod.Invoke(null, [new PathString(path)])!;
        }
    }
}
