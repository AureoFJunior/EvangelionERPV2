using EvangelionERPV2.EmailModule.Application.Services;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class EmailServiceInteractiveOAuthFallbackTests
    {
        [Fact]
        public void ShouldAttemptInteractiveGoogleOAuthFallback_WhenNotConfigured_ReturnsFalse()
        {
            var configuration = BuildConfiguration();

            var result = InvokeShouldAttemptInteractiveGoogleOAuthFallback(
                configuration,
                isWindows: true,
                isContainer: false);

            Assert.False(result);
        }

        [Theory]
        [InlineData(true, false, true)]
        [InlineData(false, false, false)]
        [InlineData(true, true, false)]
        [InlineData(false, true, false)]
        public void ShouldAttemptInteractiveGoogleOAuthFallback_WhenEnabled_RequiresWindowsOutsideContainer(
            bool isWindows,
            bool isContainer,
            bool expected)
        {
            var configuration = BuildConfiguration("true");

            var result = InvokeShouldAttemptInteractiveGoogleOAuthFallback(
                configuration,
                isWindows,
                isContainer);

            Assert.Equal(expected, result);
        }

        private static IConfiguration BuildConfiguration(string? enabled = null)
        {
            var values = new Dictionary<string, string?>();
            if (enabled != null)
                values["EmailSettings:EnableInteractiveGoogleOAuthFallback"] = enabled;

            return new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
        }

        private static bool InvokeShouldAttemptInteractiveGoogleOAuthFallback(
            IConfiguration configuration,
            bool isWindows,
            bool isContainer)
        {
            var method = typeof(EmailService).GetMethod(
                "ShouldAttemptInteractiveGoogleOAuthFallback",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(IConfiguration), typeof(bool), typeof(bool) },
                null);

            Assert.NotNull(method);

            var result = method!.Invoke(null, new object[] { configuration, isWindows, isContainer });
            return Assert.IsType<bool>(result);
        }
    }
}
