using EvangelionERPV2.Shared.Exceptions;
using Serilog;
using System.Text.Json;

namespace EvangelionERPV2.Web.Logging
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;

        public ExceptionHandlingMiddleware(RequestDelegate next)
        {
            _next = next;
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

                // Determine the appropriate response based on the exception type
                var statusCode = GetStatusCodeFromException(ex);
                var errorResponse = CreateErrorResponse(ex, statusCode);

                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";

                var errorJson = JsonSerializer.Serialize(errorResponse);
                await context.Response.WriteAsync(errorJson);
            }
        }

        private void LogException(Exception ex, HttpContext context)
        {
            // Log the exception with detailed information
            Log.Logger.Error(ex, "An unhandled exception occurred while processing the request.");
            Log.Logger.Error($"Request details: Method={context.Request.Method}, Path={context.Request.Path}");
            Log.Logger.Error($"Exception details: {ex.Message}\n{ex.StackTrace}");
        }

        private int GetStatusCodeFromException(Exception ex)
        {
            // Determine the appropriate HTTP status code based on the exception type
            if (ex is ArgumentException || ex is FormatException)
                return StatusCodes.Status400BadRequest;
            else if (ex is NotFoundDatabaseException)
                return StatusCodes.Status204NoContent;
            else
                return StatusCodes.Status500InternalServerError;
        }

        private object CreateErrorResponse(Exception ex, int statusCode)
        {
            return new
            {
                StatusCode = statusCode,
                Message = GetErrorMessage(ex, statusCode),
                Details = ex.Message
            };
        }

        private string GetErrorMessage(Exception ex, int statusCode)
        {
            switch (statusCode)
            {
                case StatusCodes.Status400BadRequest:
                    return "The request was invalid. Please check the input and try again.";
                case StatusCodes.Status404NotFound:
                    return "The requested resource could not be found.";
                case StatusCodes.Status204NoContent:
                    return "The requested content could not be found.";
                default:
                    return "An internal server error occurred. Please try again later.";
            }
        }
    }
}
