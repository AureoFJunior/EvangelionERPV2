using EvangelionERPV2.Web.Logging;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class RequestLoggingMiddlewareRedactionTests
    {
        private static readonly MethodInfo RedactSensitiveContentMethod =
            typeof(RequestLoggingMiddleware).GetMethod("RedactSensitiveContent", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("RedactSensitiveContent method was not found.");

        [Fact]
        public void RedactSensitiveContent_RedactsImagePayloadFieldsInJsonBody()
        {
            const string body = "{\"file\":\"base64-image\",\"profilePicture\":\"base64-avatar\",\"password\":\"p@ss\",\"address\":\"Avenida Paulista\"}";

            var redacted = InvokeRedactSensitiveContent(body);

            Assert.DoesNotContain("base64-image", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("base64-avatar", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("p@ss", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("Avenida Paulista", redacted, StringComparison.Ordinal);
            Assert.Contains("\"file\":\"***\"", redacted, StringComparison.Ordinal);
            Assert.Contains("\"profilePicture\":\"***\"", redacted, StringComparison.Ordinal);
            Assert.Contains("\"password\":\"***\"", redacted, StringComparison.Ordinal);
            Assert.Contains("\"address\":\"***\"", redacted, StringComparison.Ordinal);
        }

        [Fact]
        public void RedactSensitiveContent_RedactsImagePayloadFieldsInFormEncodedBody()
        {
            const string body = "file=base64-image&profilePicture=base64-avatar&password=p@ss&address=Avenida+Paulista";

            var redacted = InvokeRedactSensitiveContent(body);

            Assert.DoesNotContain("base64-image", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("base64-avatar", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("p@ss", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("Avenida+Paulista", redacted, StringComparison.Ordinal);
            Assert.Contains("file=***", redacted, StringComparison.Ordinal);
            Assert.Contains("profilePicture=***", redacted, StringComparison.Ordinal);
            Assert.Contains("password=***", redacted, StringComparison.Ordinal);
            Assert.Contains("address=***", redacted, StringComparison.Ordinal);
        }

        private static string InvokeRedactSensitiveContent(string body)
        {
            var result = RedactSensitiveContentMethod.Invoke(null, [body]);
            return Assert.IsType<string>(result);
        }
    }
}
