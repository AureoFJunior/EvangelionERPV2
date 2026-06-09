using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Serilog;

namespace EvangelionERPV2.Web.Logging
{
    public class RequestLoggingMiddleware
    {
        private const int MaxBodyLength = 10000;
        private const long MaxBodyBytesToReadForLogging = 256 * 1024;
        private static readonly string[] SensitiveJsonFields =
        [
            "password",
            "newPassword",
            "token",
            "refreshToken",
            "refresh_token",
            "authorizationCode",
            "code",
            "codeVerifier",
            "idToken",
            "id_token",
            "accessToken",
            "access_token",
            "userName",
            "email",
            "document",
            "cpf",
            "cnpj",
            "cpfCnpj",
            "phoneNumber",
            "firstName",
            "lastName",
            "adress",
            "address",
            "birthDate",
            "metadataJson",
            "clientSecret",
            "accessKey",
            "file",
            "profilePicture"
        ];

        private readonly RequestDelegate _next;
        private readonly bool _enableRequestBodyLogging;

        public RequestLoggingMiddleware(RequestDelegate next, IOptions<RequestLoggingOptions> requestLoggingOptions)
        {
            _next = next;
            _enableRequestBodyLogging = requestLoggingOptions?.Value?.EnableRequestBodyLogging ?? false;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var endpoint = SanitizeEndpointPath(context.Request.Path);
            var requestBody = "[disabled]";
            if (_enableRequestBodyLogging)
            {
                requestBody = context.User?.Identity?.IsAuthenticated == true
                    ? "[redacted: authenticated request]"
                    : await ReadRequestBodyAsync(context.Request);
            }
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

            if (request.ContentLength.HasValue && request.ContentLength.Value > MaxBodyBytesToReadForLogging)
                return $"[skipped: body too large ({request.ContentLength.Value} bytes)]";

            try
            {
                request.EnableBuffering();
                request.Body.Position = 0;

                var body = await ReadBodyWithLimitAsync(request, MaxBodyBytesToReadForLogging);
                if (body == null)
                    return "[skipped: body too large (stream)]";

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

        private static async Task<string?> ReadBodyWithLimitAsync(HttpRequest request, long maxBytes)
        {
            var buffer = new byte[8192];
            long totalRead = 0;
            using var content = new MemoryStream();

            int bytesRead;
            while ((bytesRead = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > maxBytes)
                {
                    request.Body.Position = 0;
                    return null;
                }

                content.Write(buffer, 0, bytesRead);
            }

            request.Body.Position = 0;
            return Encoding.UTF8.GetString(content.ToArray());
        }

        private static bool IsBodyLoggable(HttpRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ContentType))
                return true;

            var contentType = request.ContentType;

            if (contentType.StartsWith("application/xml", StringComparison.OrdinalIgnoreCase) ||
                contentType.StartsWith("text/xml", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
                   contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) ||
                   contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) &&
                   !contentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSensitiveEndpoint(PathString path)
        {
            var endpoint = path.Value ?? string.Empty;
            return endpoint.Contains("/User/LogInto", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/User/LoginWithGoogle", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/User/LoginWithGoogleCode", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/User/AddUser", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/User/UpdateUser", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Email/AddEmail", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Email/SendEmail", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Email/SendManualEmail", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Customer/GetCustomersByFilter", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Customer/AddCustomer", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Customer/UpdateCustomer", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Enterprise/GetEnterprisesByFilter", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Enterprise/AddEnterprise", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Enterprise/UpdateEnterprise", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/NFe/Cancel", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Order/AddOrder", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Order/InsertOrder", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Order/UpdateOrder", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Order/RefundOrder", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Product/AddProduct", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/Product/UploadPicture", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/opportunities/", StringComparison.OrdinalIgnoreCase) && endpoint.Contains("/feedback", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/PayableBills/AddPayableBill", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/PayableBills/UpdatePayableBill", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/PayableBills/RefundPayableBill", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/User/RequestPasswordReset", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/User/UpdateProfilePicture", StringComparison.OrdinalIgnoreCase) ||
                   endpoint.Contains("/User/ResetPassword", StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeEndpointPath(PathString path)
        {
            var endpoint = path.HasValue ? path.Value! : "/";
            if (string.IsNullOrWhiteSpace(endpoint))
                return "/";

            endpoint = RedactRouteValueSegment(endpoint, "/NFe/Consult/");
            endpoint = RedactRouteValueSegment(endpoint, "/NFe/Cancel/");

            return endpoint;
        }

        private static string RedactRouteValueSegment(string endpoint, string routePrefix)
        {
            var routePrefixIndex = endpoint.IndexOf(routePrefix, StringComparison.OrdinalIgnoreCase);
            if (routePrefixIndex < 0)
                return endpoint;

            var valueStartIndex = routePrefixIndex + routePrefix.Length;
            if (valueStartIndex >= endpoint.Length)
                return endpoint;

            var valueEndIndex = endpoint.IndexOf('/', valueStartIndex);
            if (valueEndIndex < 0)
                valueEndIndex = endpoint.Length;

            if (valueEndIndex <= valueStartIndex)
                return endpoint;

            return string.Concat(
                endpoint.AsSpan(0, valueStartIndex),
                "[redacted]",
                endpoint.AsSpan(valueEndIndex));
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
                "(?i)(password|newPassword|token|refreshToken|refresh_token|authorizationCode|code|codeVerifier|idToken|id_token|accessToken|access_token|userName|email|document|cpf|cnpj|cpfCnpj|phoneNumber|firstName|lastName|adress|address|birthDate|metadataJson|clientSecret|client_secret|accessKey|file|profilePicture)=([^&\\s]+)",
                "$1=***",
                RegexOptions.CultureInvariant);

            redacted = Regex.Replace(
                redacted,
                "(?i)(authorizationCode|code|codeVerifier|accessKey)=([^&\\s]+)",
                "$1=***",
                RegexOptions.CultureInvariant);

            return redacted;
        }

        private static string ResolveCaller(HttpContext context)
        {
            var user = context.User;

            if (user?.Identity?.IsAuthenticated == true)
            {
                var caller = user.FindFirst(ClaimTypes.Sid)?.Value
                             ?? user.FindFirst("uid")?.Value
                             ?? user.FindFirst("sub")?.Value;

                if (Guid.TryParse(caller, out var callerId) && callerId != Guid.Empty)
                    return callerId.ToString();

                return "authenticated";
            }

            var remoteIp = context.Connection.RemoteIpAddress?.ToString();
            if (string.IsNullOrWhiteSpace(remoteIp))
                return "anonymous";

            return $"anonymous:{CreateStableFingerprint(remoteIp)}";
        }

        private static string CreateStableFingerprint(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
            return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
        }
    }
}
