using EvangelionERPV2.Shared.Exceptions;
using Microsoft.AspNetCore.Mvc;
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
            Log.Logger.Error(
                ex,
                "Unhandled exception. Method={Method}, Path={Path}, TraceId={TraceId}",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier);
        }

        private static int GetStatusCodeFromException(Exception ex)
        {
            return ex switch
            {
                ArgumentException or FormatException => StatusCodes.Status400BadRequest,
                NotFoundDatabaseException => StatusCodes.Status204NoContent,
                _ => StatusCodes.Status500InternalServerError
            };
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
                return ex.Message;

            if (statusCode == StatusCodes.Status500InternalServerError && _environment.IsDevelopment())
                return ex.Message;

            return null;
        }

        private static string GetErrorMessage(int statusCode)
        {
            return statusCode switch
            {
                StatusCodes.Status400BadRequest => "The request was invalid. Please check the input and try again.",
                StatusCodes.Status500InternalServerError => "An internal server error occurred. Please try again later.",
                _ => "An unexpected error occurred."
            };
        }
    }
}
