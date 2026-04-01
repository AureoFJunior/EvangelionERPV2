using EvangelionERPV2.EmailModule.Application.Services;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class EmailServiceSafeExceptionTypeTests
    {
        [Fact]
        public void GetSafeExceptionType_ReturnsExceptionTypeName_WithoutMessageContent()
        {
            var result = InvokeGetSafeExceptionType(new InvalidOperationException("smtp authentication failed for sender@example.com"));

            Assert.Equal(nameof(InvalidOperationException), result);
            Assert.DoesNotContain("sender@example.com", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("smtp authentication failed", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetSafeExceptionType_WhenNull_ReturnsUnknownError()
        {
            var result = InvokeGetSafeExceptionType(null);

            Assert.Equal("UnknownError", result);
        }

        private static string InvokeGetSafeExceptionType(Exception? exception)
        {
            var method = typeof(EmailService).GetMethod("GetSafeExceptionType", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var result = method!.Invoke(null, new object?[] { exception });
            return Assert.IsType<string>(result);
        }
    }
}
