using EvangelionERPV2.Web.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;

namespace EvangelionERPV2.Test.Security
{
    [Collection("Serilog global logger")]
    public class RequestLoggingMiddlewarePrivacyTests
    {
        [Fact]
        public async Task InvokeAsync_RedactsSensitiveFieldsFromRequestBody()
        {
            var sink = new CollectingSink();
            var previousLogger = Log.Logger;
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();

            try
            {
                var middleware = new RequestLoggingMiddleware(
                    _ => Task.CompletedTask,
                    Options.Create(new RequestLoggingOptions
                    {
                        EnableRequestBodyLogging = true
                    }));

                var context = new DefaultHttpContext();
                var payload = """
                {"password":"secret","authorizationCode":"auth-code","codeVerifier":"verifier","accessKey":"123456","description":"ok"}
                """;

                context.Request.Method = HttpMethods.Post;
                context.Request.Path = "/api/v1/Reports/Generate";
                context.Request.ContentType = "application/json";
                context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));

                await middleware.InvokeAsync(context);

                var requestLogEvent = sink.Events
                    .Single(logEvent => logEvent.RenderMessage().Contains("Request: Endpoint="));
                var requestBody = (requestLogEvent.Properties["RequestBody"] as ScalarValue)?.Value?.ToString();

                Assert.NotNull(requestBody);
                Assert.Contains("\"password\":\"***\"", requestBody, StringComparison.Ordinal);
                Assert.Contains("\"authorizationCode\":\"***\"", requestBody, StringComparison.Ordinal);
                Assert.Contains("\"codeVerifier\":\"***\"", requestBody, StringComparison.Ordinal);
                Assert.Contains("\"accessKey\":\"***\"", requestBody, StringComparison.Ordinal);
                Assert.DoesNotContain("secret", requestBody, StringComparison.Ordinal);
                Assert.DoesNotContain("auth-code", requestBody, StringComparison.Ordinal);
                Assert.DoesNotContain("verifier", requestBody, StringComparison.Ordinal);
            }
            finally
            {
                Log.Logger = previousLogger;
            }
        }

        [Fact]
        public async Task InvokeAsync_SkipsXmlRequestBodies()
        {
            var sink = new CollectingSink();
            var previousLogger = Log.Logger;
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();

            try
            {
                var middleware = new RequestLoggingMiddleware(
                    _ => Task.CompletedTask,
                    Options.Create(new RequestLoggingOptions
                    {
                        EnableRequestBodyLogging = true
                    }));

                var context = new DefaultHttpContext();
                const string payload = "<payload><password>secret</password></payload>";

                context.Request.Method = HttpMethods.Post;
                context.Request.Path = "/api/v1/Reports/Generate";
                context.Request.ContentType = "application/xml";
                context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));

                await middleware.InvokeAsync(context);

                var renderedMessage = sink.Events
                    .Select(x => x.RenderMessage())
                    .Single(message => message.Contains("Request: Endpoint="));

                Assert.DoesNotContain("secret", renderedMessage, StringComparison.Ordinal);
                Assert.DoesNotContain("<password>", renderedMessage, StringComparison.Ordinal);
                Assert.Contains("[skipped: application/xml]", renderedMessage, StringComparison.Ordinal);
            }
            finally
            {
                Log.Logger = previousLogger;
            }
        }

        [Fact]
        public async Task InvokeAsync_SkipsPlainTextRequestBodies()
        {
            var sink = new CollectingSink();
            var previousLogger = Log.Logger;
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();

            try
            {
                var middleware = new RequestLoggingMiddleware(
                    _ => Task.CompletedTask,
                    Options.Create(new RequestLoggingOptions
                    {
                        EnableRequestBodyLogging = true
                    }));

                var context = new DefaultHttpContext();
                const string payload = "token=super-secret-value";

                context.Request.Method = HttpMethods.Post;
                context.Request.Path = "/api/v1/Reports/Generate";
                context.Request.ContentType = "text/plain";
                context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));

                await middleware.InvokeAsync(context);

                var renderedMessage = sink.Events
                    .Select(x => x.RenderMessage())
                    .Single(message => message.Contains("Request: Endpoint="));

                Assert.DoesNotContain("super-secret-value", renderedMessage, StringComparison.Ordinal);
                Assert.Contains("[skipped: text/plain]", renderedMessage, StringComparison.Ordinal);
            }
            finally
            {
                Log.Logger = previousLogger;
            }
        }

        [Fact]
        public async Task InvokeAsync_WhenUserIsAuthenticated_RedactsRequestBody()
        {
            var sink = new CollectingSink();
            var previousLogger = Log.Logger;
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();

            try
            {
                var middleware = new RequestLoggingMiddleware(
                    _ => Task.CompletedTask,
                    Options.Create(new RequestLoggingOptions
                    {
                        EnableRequestBodyLogging = true
                    }));

                var context = new DefaultHttpContext();
                const string payload = "{\"description\":\"allowed\",\"email\":\"customer@example.com\"}";

                context.Request.Method = HttpMethods.Post;
                context.Request.Path = "/api/v1/Reports/Generate";
                context.Request.ContentType = "application/json";
                context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Sid, Guid.NewGuid().ToString())
                ], "TestAuth"));

                await middleware.InvokeAsync(context);

                var requestLogEvent = sink.Events
                    .Single(logEvent => logEvent.RenderMessage().Contains("Request: Endpoint="));
                var requestBody = (requestLogEvent.Properties["RequestBody"] as ScalarValue)?.Value?.ToString();

                Assert.Equal("[redacted: authenticated request]", requestBody);
                Assert.DoesNotContain("customer@example.com", requestLogEvent.RenderMessage(), StringComparison.Ordinal);
            }
            finally
            {
                Log.Logger = previousLogger;
            }
        }

        private sealed class CollectingSink : ILogEventSink
        {
            private readonly ConcurrentQueue<LogEvent> _events = new();

            public IReadOnlyCollection<LogEvent> Events => _events.ToArray();

            public void Emit(LogEvent logEvent)
            {
                _events.Enqueue(logEvent);
            }
        }
    }

    [CollectionDefinition("Serilog global logger", DisableParallelization = true)]
    public sealed class SerilogGlobalLoggerCollection
    {
    }
}
