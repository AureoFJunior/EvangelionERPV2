using EvangelionERPV2.Web.Logging;
using Microsoft.Extensions.Hosting;
using Moq;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class ExceptionHandlingMiddlewareLoggingPolicyTests
    {
        [Fact]
        public void ShouldLogExceptionDetails_ReturnsFalse_ForArgumentException()
        {
            var result = InvokeShouldLogExceptionDetails(new ArgumentException("invalid value"));
            Assert.False(result);
        }

        [Fact]
        public void ShouldLogExceptionDetails_ReturnsFalse_ForFormatException()
        {
            var result = InvokeShouldLogExceptionDetails(new FormatException("bad number format"));
            Assert.False(result);
        }

        [Fact]
        public void ShouldLogExceptionDetails_ReturnsTrue_ForServerException()
        {
            var result = InvokeShouldLogExceptionDetails(new InvalidOperationException("database timeout"));
            Assert.True(result);
        }

        [Fact]
        public void GetSafeExceptionType_ReturnsTypeName_WithoutMessageContent()
        {
            var result = InvokeGetSafeExceptionType(new InvalidOperationException("token=abc123"));

            Assert.Equal(nameof(InvalidOperationException), result);
            Assert.DoesNotContain("abc123", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token=", result, StringComparison.OrdinalIgnoreCase);
        }

        private static bool InvokeShouldLogExceptionDetails(Exception ex)
        {
            var method = typeof(ExceptionHandlingMiddleware).GetMethod(
                "ShouldLogExceptionDetails",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var result = method!.Invoke(null, [ex]);
            return Assert.IsType<bool>(result);
        }

        private static string InvokeGetSafeExceptionType(Exception? ex)
        {
            var method = typeof(ExceptionHandlingMiddleware).GetMethod(
                "GetSafeExceptionType",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var result = method!.Invoke(null, new object?[] { ex });
            return Assert.IsType<string>(result);
        }
    }
}
