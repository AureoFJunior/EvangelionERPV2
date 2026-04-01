using EvangelionERPV2.Web.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Moq;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class ExceptionHandlingMiddlewareErrorDetailTests
    {
        [Fact]
        public void GetErrorDetail_WhenBadRequestHasInnerException_ReturnsGenericMessage()
        {
            var middleware = CreateMiddleware(isDevelopment: false);
            var exception = new ArgumentException(
                "Database error: Server=prod-db;User Id=admin;",
                new InvalidOperationException("Sensitive provider details"));

            var detail = InvokeGetErrorDetail(middleware, exception, StatusCodes.Status400BadRequest);

            Assert.Equal("The request could not be processed due to invalid input.", detail);
            Assert.DoesNotContain("Server=prod-db", detail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("provider details", detail, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetErrorDetail_WhenBadRequestHasNoInnerException_ReturnsGenericMessage()
        {
            var middleware = CreateMiddleware(isDevelopment: false);
            var exception = new ArgumentException("Email is required.");

            var detail = InvokeGetErrorDetail(middleware, exception, StatusCodes.Status400BadRequest);

            Assert.Equal("The request could not be processed due to invalid input.", detail);
            Assert.DoesNotContain("Email is required.", detail, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetErrorDetail_WhenInternalServerErrorInDevelopment_ReturnsNull()
        {
            var middleware = CreateMiddleware(isDevelopment: true);
            var exception = new InvalidOperationException("Connection string contains secret.");

            var detail = InvokeGetErrorDetail(middleware, exception, StatusCodes.Status500InternalServerError);

            Assert.Null(detail);
        }

        private static ExceptionHandlingMiddleware CreateMiddleware(bool isDevelopment)
        {
            var environment = new Mock<IHostEnvironment>(MockBehavior.Strict);
            environment.SetupGet(x => x.EnvironmentName).Returns(isDevelopment ? Environments.Development : Environments.Production);

            return new ExceptionHandlingMiddleware(_ => Task.CompletedTask, environment.Object);
        }

        private static string? InvokeGetErrorDetail(ExceptionHandlingMiddleware middleware, Exception ex, int statusCode)
        {
            var method = typeof(ExceptionHandlingMiddleware).GetMethod("GetErrorDetail", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            return method!.Invoke(middleware, [ex, statusCode]) as string;
        }
    }
}
