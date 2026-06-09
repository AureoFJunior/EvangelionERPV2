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

        [Fact]
        public void RedactSensitiveContent_RedactsOAuthTokenFieldVariants()
        {
            const string body = "{\"accessToken\":\"abc123\",\"access_token\":\"def456\",\"refresh_token\":\"ghi789\",\"id_token\":\"jkl012\"}";

            var redacted = InvokeRedactSensitiveContent(body);

            Assert.DoesNotContain("abc123", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("def456", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("ghi789", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("jkl012", redacted, StringComparison.Ordinal);
            Assert.Contains("\"accessToken\":\"***\"", redacted, StringComparison.Ordinal);
            Assert.Contains("\"access_token\":\"***\"", redacted, StringComparison.Ordinal);
            Assert.Contains("\"refresh_token\":\"***\"", redacted, StringComparison.Ordinal);
            Assert.Contains("\"id_token\":\"***\"", redacted, StringComparison.Ordinal);
        }

        [Fact]
        public void RedactSensitiveContent_RedactsOAuthTokenFieldVariantsInFormEncodedBody()
        {
            const string body = "accessToken=abc123&access_token=def456&refresh_token=ghi789&id_token=jkl012&authorizationCode=mno345&codeVerifier=pqr678";

            var redacted = InvokeRedactSensitiveContent(body);

            Assert.DoesNotContain("abc123", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("def456", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("ghi789", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("jkl012", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("mno345", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("pqr678", redacted, StringComparison.Ordinal);
            Assert.Contains("accessToken=***", redacted, StringComparison.Ordinal);
            Assert.Contains("access_token=***", redacted, StringComparison.Ordinal);
            Assert.Contains("refresh_token=***", redacted, StringComparison.Ordinal);
            Assert.Contains("id_token=***", redacted, StringComparison.Ordinal);
            Assert.Contains("authorizationCode=***", redacted, StringComparison.Ordinal);
            Assert.Contains("codeVerifier=***", redacted, StringComparison.Ordinal);
        }

        private static string InvokeRedactSensitiveContent(string body)
        {
            var result = RedactSensitiveContentMethod.Invoke(null, [body]);
            return Assert.IsType<string>(result);
        }
    }
}
