using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Web.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Moq;
using System.Reflection;
using System.Text.Json;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Pins the 503 contract for a broker that will not accept a publish. A regression here is
    /// silent: callers still see an error, but nobody retries.
    /// </summary>
    public class ExceptionHandlingMiddlewareBrokerUnavailableTests
    {
        [Fact]
        public void GetStatusCodeFromException_Returns503_ForBrokerUnavailable()
        {
            var middleware = CreateMiddleware();
            var exception = new MessageBrokerUnavailableException("broker refused", new Exception("inner"));

            var statusCode = InvokeGetStatusCodeFromException(middleware, exception);

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusCode);
        }

        [Fact]
        public void GetStatusCodeFromException_DoesNotFallThroughTo500()
        {
            var middleware = CreateMiddleware();
            var exception = new MessageBrokerUnavailableException("broker refused", new Exception("inner"));

            var statusCode = InvokeGetStatusCodeFromException(middleware, exception);

            Assert.NotEqual(StatusCodes.Status500InternalServerError, statusCode);
        }

        [Fact]
        public void GetErrorMessage_ForServiceUnavailable_SaysRetryAndLeaksNothing()
        {
            var message = InvokeGetErrorMessage(StatusCodes.Status503ServiceUnavailable);

            Assert.Equal("The service is temporarily unable to accept this request. Please retry shortly.", message);

            Assert.DoesNotContain("rabbit", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("broker", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("queue", message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A payload that will not serialize is a caller bug, not an unavailable broker. Reported as
        /// 503 it would carry Retry-After and invite a client to retry forever.
        /// </summary>
        [Fact]
        public void SerializationFailure_IsNotReportedAsRetryable()
        {
            var middleware = CreateMiddleware();

            var statusCode = InvokeGetStatusCodeFromException(middleware, new JsonException("cycle detected"));

            Assert.NotEqual(StatusCodes.Status503ServiceUnavailable, statusCode);
            Assert.Equal(StatusCodes.Status500InternalServerError, statusCode);
        }

        [Fact]
        public void ShouldLogExceptionDetails_IsTrue_ForBrokerUnavailable()
        {
            var exception = new MessageBrokerUnavailableException("broker refused", new Exception("inner"));

            Assert.True(InvokeShouldLogExceptionDetails(exception));
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

        private static bool InvokeShouldLogExceptionDetails(Exception ex)
        {
            var method = typeof(ExceptionHandlingMiddleware).GetMethod("ShouldLogExceptionDetails", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (bool)method!.Invoke(null, [ex])!;
        }
    }
}
