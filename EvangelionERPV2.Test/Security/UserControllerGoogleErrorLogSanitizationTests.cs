using EvangelionERPV2.Web.Controllers;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class UserControllerGoogleErrorLogSanitizationTests
    {
        [Fact]
        public void GetSafeGoogleProviderErrorCode_WhenNullOrWhitespace_ReturnsUnknown()
        {
            Assert.Equal("unknown", InvokeGetSafeGoogleProviderErrorCode(null));
            Assert.Equal("unknown", InvokeGetSafeGoogleProviderErrorCode("   "));
        }

        [Fact]
        public void GetSafeGoogleProviderErrorCode_RemovesUnsafeCharacters_AndTruncates()
        {
            var input = "invalid_grant \"token=abc\" <script>alert(1)</script> ########################";

            var value = InvokeGetSafeGoogleProviderErrorCode(input);

            Assert.DoesNotContain(" ", value, StringComparison.Ordinal);
            Assert.DoesNotContain("\"", value, StringComparison.Ordinal);
            Assert.DoesNotContain("<", value, StringComparison.Ordinal);
            Assert.DoesNotContain(">", value, StringComparison.Ordinal);
            Assert.True(value.Length <= 64);
        }

        [Fact]
        public void GetSafeGoogleProviderErrorCode_PreservesSafeProviderCode()
        {
            var value = InvokeGetSafeGoogleProviderErrorCode("invalid_grant");
            Assert.Equal("invalid_grant", value);
        }

        private static string InvokeGetSafeGoogleProviderErrorCode(string? providerError)
        {
            var method = typeof(UserController).GetMethod("GetSafeGoogleProviderErrorCode", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var result = method!.Invoke(null, [providerError]);
            return Assert.IsType<string>(result);
        }
    }
}
