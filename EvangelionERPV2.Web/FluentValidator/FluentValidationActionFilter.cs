using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EvangelionERPV2.Web.FluentValidator
{
    /// <summary>
    /// Runs the registered <see cref="IValidator{T}"/> for every bound action argument and writes
    /// the failures into <c>ModelState</c>, which is what the controllers read.
    /// </summary>
    /// <remarks>
    /// Registered ahead of MVC's own <c>ModelStateInvalidFilter</c> (order -2000) so that a
    /// <c>[ApiController]</c> action still answers 400 automatically on a validation failure, the
    /// way it did while <c>FluentValidation.AspNetCore</c> validated during model binding.
    /// </remarks>
    public sealed class FluentValidationActionFilter : IAsyncActionFilter
    {
        public const int FilterOrder = -2001;

        private readonly IServiceProvider _services;

        public FluentValidationActionFilter(IServiceProvider services)
        {
            _services = services;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            foreach (var argument in context.ActionArguments.Values)
            {
                if (argument is null)
                    continue;

                if (_services.GetService(typeof(IValidator<>).MakeGenericType(argument.GetType())) is not IValidator validator)
                    continue;

                var result = await validator.ValidateAsync(new ValidationContext<object>(argument));

                foreach (var failure in result.Errors)
                    context.ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            await next();
        }
    }
}
