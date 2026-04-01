using EvangelionERPV2.EmailModule.Application.Services;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class EmailServiceLogSafeIdentifierTests
    {
        [Fact]
        public void GetLogSafeEmailIdentifier_DoesNotExposeRawEmail_AndIsDeterministic()
        {
            var hashA = InvokeGetLogSafeEmailIdentifier("  sender@example.com  ");
            var hashB = InvokeGetLogSafeEmailIdentifier("sender@example.com");

            Assert.Equal(12, hashA.Length);
            Assert.Matches("^[a-f0-9]{12}$", hashA);
            Assert.Equal(hashA, hashB);
            Assert.DoesNotContain("sender@example.com", hashA, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetLogSafeEmailIdentifier_WhenEmpty_ReturnsEmptyMarker()
        {
            var hash = InvokeGetLogSafeEmailIdentifier("   ");
            Assert.Equal("empty", hash);
        }

        private static string InvokeGetLogSafeEmailIdentifier(string? email)
        {
            var method = typeof(EmailService).GetMethod("GetLogSafeEmailIdentifier", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var result = method.Invoke(null, new object?[] { email });
            return Assert.IsType<string>(result);
        }
    }
}
