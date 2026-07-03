using EvangelionERPV2.Shared.Entities.RabbitMQ;
using EvangelionERPV2.Shared.Utils;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class RabbitMQManagerBaseSafeExceptionTypeTests
    {
        [Fact]
        public void GetSafeExceptionType_ReturnsExceptionTypeName_WithoutSensitiveMessage()
        {
            var result = InvokeGetSafeExceptionType(new InvalidOperationException("token=abc123 should not leak"));

            Assert.Equal(nameof(InvalidOperationException), result);
            Assert.DoesNotContain("abc123", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token=", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetSafeExceptionType_WhenNull_ReturnsUnknownError()
        {
            var result = InvokeGetSafeExceptionType(null);

            Assert.Equal("UnknownError", result);
        }

        private static string InvokeGetSafeExceptionType(Exception? exception)
        {
            var method = typeof(RabbitMQManagerBase<BaseChannelSettings>).GetMethod(
                "GetSafeExceptionType",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var result = method!.Invoke(null, new object?[] { exception });
            return Assert.IsType<string>(result);
        }
    }
}
