using EvangelionERPV2.Web.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Moq;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class ExceptionHandlingMiddlewareUnauthorizedTests
    {
        [Fact]
        public void GetStatusCodeFromException_Returns401_ForUnauthorizedAccessException()
        {
            var middleware = CreateMiddleware();
            var statusCode = InvokeGetStatusCodeFromException(middleware, new UnauthorizedAccessException("denied"));

            Assert.Equal(StatusCodes.Status401Unauthorized, statusCode);
        }

        [Fact]
        public void GetErrorMessage_ReturnsGenericUnauthorizedMessage()
        {
            var message = InvokeGetErrorMessage(StatusCodes.Status401Unauthorized);

            Assert.Equal("The request could not be authorized.", message);
        }

        private static ExceptionHandlingMiddleware CreateMiddleware()
        {
            var environment = new Mock<IHostEnvironment>(MockBehavior.Strict);
            environment.SetupGet(x => x.EnvironmentName).Returns(Environments.Production);

            return new ExceptionHandlingMiddleware(_ => Task.CompletedTask, environment.Object);
        }

        private static int InvokeGetStatusCodeFromException(ExceptionHandlingMiddleware middleware, Exception ex)
        {
            var method = typeof(ExceptionHandlingMiddleware).GetMethod("GetStatusCodeFromException", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (int)method!.Invoke(null, [ex])!;
        }

        private static string InvokeGetErrorMessage(int statusCode)
        {
            var method = typeof(ExceptionHandlingMiddleware).GetMethod("GetErrorMessage", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (string)method!.Invoke(null, [statusCode])!;
        }
    }
}
