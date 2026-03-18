using System.Diagnostics;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Serilog;

namespace EvangelionERPV2.Web.Logging
{
    public class RequestLoggingMiddleware
    {
        private const int MaxBodyLength = 10000;
        private static readonly string[] SensitiveJsonFields =
        [
            "password",
            "newPassword",
            "token",
            "refreshToken",
            "idToken",
            "clientSecret"
        ];

        private readonly RequestDelegate _next;

        public RequestLoggingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var endpoint = context.Request.Path.HasValue ? context.Request.Path.Value : "/";
            var requestBody = await ReadRequestBodyAsync(context.Request);
            var startTimestamp = Stopwatch.GetTimestamp();

            try
            {
                await _next(context);
            }
            finally
            {
                var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                var caller = ResolveCaller(context);
                var statusCode = context.Response.StatusCode;

                Log.Logger.Information(
                    "Request: Endpoint={Endpoint}, Caller={Caller}, RequestBody={RequestBody}, ResponseTimeMs={ResponseTimeMs}, StatusCode={StatusCode}",
                    endpoint,
                    caller,
                    requestBody,
                    Math.Round(elapsedMs, 2),
                    statusCode);
            }
        }

        private static async Task<string> ReadRequestBodyAsync(HttpRequest request)
        {
            if (!IsBodyLoggable(request))
                return $"[skipped: {request.ContentType}]";

            try
            {
                request.EnableBuffering();
                request.Body.Position = 0;

                using var reader = new StreamReader(request.Body, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                request.Body.Position = 0;

                if (string.IsNullOrWhiteSpace(body))
                    return string.Empty;

                var sanitized = IsSensitiveEndpoint(request.Path)
                    ? "[redacted: sensitive endpoint]"
                    : RedactSensitiveContent(body);

                return sanitized.Length <= MaxBodyLength
                    ? sanitized
                    : $"{sanitized[..MaxBodyLength]}... [truncated]";
            }
            catch (Exception ex)
            {
                return $"[failed to read body: {ex.GetType().Name}]";
            }
        }

        private static bool IsBodyLoggable(HttpRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ContentType))
                return true;

            var contentType = request.ContentType;

            return contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
                   contentType.StartsWith("application/xml", StringComparison.OrdinalIgnoreCase) ||
                   contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) ||
                   contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSensitiveEndpoint(PathString path)
        {
            var endpoint = path.Value ?? string.Empty;
            return endpoint.Contains("/User/LogInto", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/User/RequestPasswordReset", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/User/ResetPassword", StringComparison.OrdinalIgnoreCase);
        }

        private static string RedactSensitiveContent(string body)
        {
            var redacted = body;

            foreach (var field in SensitiveJsonFields)
            {
                redacted = Regex.Replace(
                    redacted,
                    $"(\"{Regex.Escape(field)}\"\\s*:\\s*\")([^\"]*)(\")",
                    "$1***$3",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            redacted = Regex.Replace(
                redacted,
                "(?i)(password|newPassword|token|refreshToken|idToken|clientSecret)=([^&\\s]+)",
                "$1=***",
                RegexOptions.CultureInvariant);

            return redacted;
        }

        private static string ResolveCaller(HttpContext context)
        {
            var user = context.User;

            if (user?.Identity?.IsAuthenticated == true)
            {
                var caller = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? user.FindFirst("sub")?.Value
                             ?? user.FindFirst(ClaimTypes.Name)?.Value
                             ?? user.FindFirst(ClaimTypes.Email)?.Value
                             ?? user.Identity.Name;

                if (!string.IsNullOrWhiteSpace(caller))
                    return caller;
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        }
    }
}
