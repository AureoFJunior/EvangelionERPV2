using Amazon.SecretsManager;
using AutoMapper;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Net;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class UserControllerResetPasswordRateLimitKeyTests
    {
        [Fact]
        public void BuildResetPasswordRateLimitKey_DoesNotExposeRawEmailOrIp_AndUsesFixedLengthHash()
        {
            var controller = CreateController();
            SetHttpContext(controller, "203.0.113.25");

            var key = InvokeBuildResetPasswordRateLimitKey(controller, "joao.silva@example.com");

            Assert.Equal(64, key.Length);
            Assert.Matches("^[a-f0-9]{64}$", key);
            Assert.DoesNotContain("joao.silva@example.com", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("203.0.113.25", key, StringComparison.Ordinal);
        }

        [Fact]
        public void BuildResetPasswordRateLimitKey_NormalizesEmailCaseAndWhitespace()
        {
            var controller = CreateController();
            SetHttpContext(controller, "203.0.113.25");

            var keyA = InvokeBuildResetPasswordRateLimitKey(controller, "  Joao.Silva@Example.com  ");
            var keyB = InvokeBuildResetPasswordRateLimitKey(controller, "joao.silva@example.com");

            Assert.Equal(keyA, keyB);
        }

        [Fact]
        public void GetLogSafeEmailIdentifier_DoesNotExposeRawEmail_AndUsesShortDeterministicHash()
        {
            var hashA = InvokeGetLogSafeEmailIdentifier("  Joao.Silva@Example.com  ");
            var hashB = InvokeGetLogSafeEmailIdentifier("joao.silva@example.com");

            Assert.Equal(12, hashA.Length);
            Assert.Matches("^[a-f0-9]{12}$", hashA);
            Assert.Equal(hashA, hashB);
            Assert.DoesNotContain("joao.silva@example.com", hashA, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetLogSafeEmailIdentifier_WhenEmpty_ReturnsEmptyMarker()
        {
            var hash = InvokeGetLogSafeEmailIdentifier("   ");
            Assert.Equal("empty", hash);
        }

        [Fact]
        public void ResolveCallerIpAddress_UsesRemoteIp_IgnoresForwardedHeaders()
        {
            var controller = CreateController();
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.10");
            context.Request.Headers["X-Real-IP"] = "203.0.113.7";
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.8";

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = context
            };

            var callerIp = InvokeResolveCallerIpAddress(controller);

            Assert.Equal("198.51.100.10", callerIp);
        }

        [Fact]
        public void ResolveCallerIpAddress_WhenRemoteIpMissing_ReturnsUnknown()
        {
            var controller = CreateController();
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            var callerIp = InvokeResolveCallerIpAddress(controller);

            Assert.Equal("unknown", callerIp);
        }

        private static UserController CreateController()
        {
            var userService = new Mock<IUserService<User>>(MockBehavior.Strict).Object;
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict).Object;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var mapper = new Mock<IMapper>(MockBehavior.Strict).Object;
            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>(MockBehavior.Strict).Object, configuration);
            var refreshTokenRepository = new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object;
            var tokenService = new TokenService(refreshTokenRepository, configuration);
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict).Object;

            return new UserController(
                userService,
                userRepository,
                mapper,
                configuration,
                kmsProvider,
                tokenService,
                emailService,
                new RecaptchaVerifier(new HttpClient(), configuration, kmsProvider));
        }

        private static void SetHttpContext(Controller controller, string ipAddress)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["X-Real-IP"] = ipAddress;
            context.Connection.RemoteIpAddress = IPAddress.Parse(ipAddress);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = context
            };
        }

        private static string InvokeBuildResetPasswordRateLimitKey(UserController controller, string? email)
        {
            var method = typeof(UserController).GetMethod("BuildResetPasswordRateLimitKey", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var result = method.Invoke(controller, new object?[] { email });
            return Assert.IsType<string>(result);
        }

        private static string InvokeGetLogSafeEmailIdentifier(string? email)
        {
            var method = typeof(UserController).GetMethod("GetLogSafeEmailIdentifier", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var result = method.Invoke(null, new object?[] { email });
            return Assert.IsType<string>(result);
        }

        private static string InvokeResolveCallerIpAddress(UserController controller)
        {
            var method = typeof(UserController).GetMethod("ResolveCallerIpAddress", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var result = method.Invoke(controller, null);
            return Assert.IsType<string>(result);
        }
    }
}
