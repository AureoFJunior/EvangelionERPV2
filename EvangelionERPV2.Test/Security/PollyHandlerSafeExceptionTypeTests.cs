using EvangelionERPV2.Shared.Utils;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class PollyHandlerSafeExceptionTypeTests
    {
        [Fact]
        public void GetSafeExceptionType_ReturnsTypeName_AndNotRawMessage()
        {
            var exception = new InvalidOperationException("Token=abc123 leaked in message");

            var result = InvokeGetSafeExceptionType(exception);

            Assert.Equal(nameof(InvalidOperationException), result);
            Assert.DoesNotContain("abc123", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Token=", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetSafeExceptionType_WhenNull_ReturnsUnknownError()
        {
            var result = InvokeGetSafeExceptionType(null);

            Assert.Equal("UnknownError", result);
        }

        private static string InvokeGetSafeExceptionType(Exception? exception)
        {
            var method = typeof(PollyHandler).GetMethod("GetSafeExceptionType", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var result = method!.Invoke(null, new object?[] { exception });
            return Assert.IsType<string>(result);
        }
    }
}
