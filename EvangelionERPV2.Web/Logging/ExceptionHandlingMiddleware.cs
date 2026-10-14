using EvangelionERPV2.Shared.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text.Json;

namespace EvangelionERPV2.Web.Logging
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IHostEnvironment _environment;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ExceptionHandlingMiddleware(RequestDelegate next, IHostEnvironment environment)
        {
            _next = next;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                LogException(ex, context);
                var statusCode = GetStatusCodeFromException(ex);

                if (context.Response.HasStarted)
                {
                    Log.Logger.Warning(
                        "Response already started. Exception middleware cannot write error response. TraceId={TraceId}",
                        context.TraceIdentifier);
                    throw;
                }

                context.Response.Clear();

                context.Response.StatusCode = statusCode;

                // Keep 204 fully bodyless by HTTP contract.
                if (statusCode == StatusCodes.Status204NoContent)
                    return;

                var problem = CreateProblemDetails(ex, statusCode, context.TraceIdentifier);
                context.Response.ContentType = "application/problem+json";

                var payload = JsonSerializer.Serialize(problem, JsonOptions);
                await context.Response.WriteAsync(payload);
            }
        }

        private static void LogException(Exception ex, HttpContext context)
        {
            var sanitizedPath = SanitizeEndpointPath(context.Request.Path);

            if (ShouldLogExceptionDetails(ex))
            {
                Log.Logger.Error(
                    "Unhandled exception. Method={Method}, Path={Path}, TraceId={TraceId}, ErrorType={ErrorType}",
                    context.Request.Method,
                    sanitizedPath,
                    context.TraceIdentifier,
                    GetSafeExceptionType(ex));
                return;
            }

            Log.Logger.Warning(
                "Handled client exception. Method={Method}, Path={Path}, TraceId={TraceId}, ErrorType={ErrorType}",
                context.Request.Method,
                sanitizedPath,
                context.TraceIdentifier,
                GetSafeExceptionType(ex));
        }

        private static int GetStatusCodeFromException(Exception ex)
        {
            return ex switch
            {
                ArgumentException or FormatException => StatusCodes.Status400BadRequest,
                UnauthorizedAccessException or SecurityTokenException => StatusCodes.Status401Unauthorized,
                NotFoundDatabaseException => StatusCodes.Status204NoContent,
                _ => StatusCodes.Status500InternalServerError
            };
        }

        private static bool ShouldLogExceptionDetails(Exception ex)
        {
            return ex is not (ArgumentException or FormatException or NotFoundDatabaseException or UnauthorizedAccessException or SecurityTokenException);
        }

        private ProblemDetails CreateProblemDetails(Exception ex, int statusCode, string traceId)
        {
            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = GetErrorMessage(statusCode),
                Detail = GetErrorDetail(ex, statusCode)
            };

            problem.Extensions["traceId"] = traceId;
            return problem;
        }

        private string? GetErrorDetail(Exception ex, int statusCode)
        {
            if (statusCode == StatusCodes.Status400BadRequest)
                return GetSafeBadRequestDetail(ex);

            if (statusCode == StatusCodes.Status401Unauthorized)
                return null;

            if (statusCode == StatusCodes.Status500InternalServerError)
                return null;

            return null;
        }

        private static string GetSafeBadRequestDetail(Exception ex)
        {
            return "The request could not be processed due to invalid input.";
        }

        private static string GetErrorMessage(int statusCode)
        {
            return statusCode switch
            {
                StatusCodes.Status400BadRequest => "The request was invalid. Please check the input and try again.",
                StatusCodes.Status401Unauthorized => "The request could not be authorized.",
                StatusCodes.Status500InternalServerError => "An internal server error occurred. Please try again later.",
                _ => "An unexpected error occurred."
            };
        }

        private static string GetSafeExceptionType(Exception? exception)
        {
            return exception?.GetType().Name ?? "UnknownError";
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
    }
}
