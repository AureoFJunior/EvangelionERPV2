using EvangelionERPV2.Shared.Utils;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class SharedFunctionsRequestUriValidationTests
    {
        [Fact]
        public void BuildRequestUri_WithTraversalSegments_Throws()
        {
            var exception = Assert.Throws<TargetInvocationException>(() =>
                InvokeBuildRequestUri("https://example.com/api/v1", "Order/InsertOrder", "../admin"));

            Assert.IsType<ArgumentException>(exception.InnerException);
        }

        [Fact]
        public void BuildRequestUri_WithValidSegments_EscapesPathValues()
        {
            var result = InvokeBuildRequestUri("https://example.com/api/v1", "Order/InsertOrder", "customer name");

            Assert.Equal("https://example.com/api/v1/Order/InsertOrder/customer%20name", result.AbsoluteUri);
        }

        [Fact]
        public void BuildRequestUri_WithNonHttpBaseUrl_Throws()
        {
            var exception = Assert.Throws<TargetInvocationException>(() =>
                InvokeBuildRequestUri("ftp://example.com", "Order/InsertOrder", null));

            Assert.IsType<ArgumentException>(exception.InnerException);
        }

        private static Uri InvokeBuildRequestUri(string apiBaseUrl, string apiEndpoint, string? parameters)
        {
            var method = typeof(SharedFunctions).GetMethod(
                "BuildRequestUri",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);

            return (Uri)method!.Invoke(null, new object?[] { apiBaseUrl, apiEndpoint, parameters })!;
        }
    }
}
