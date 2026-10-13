using EvangelionERPV2.Web.FluentValidator;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The controllers read <c>ModelState</c> and nothing else. This is the proof that the filter
    /// which replaced <c>FluentValidation.AspNetCore</c>'s auto-validation still fills it - without
    /// it every registered validator would be dead code and every payload would pass.
    /// </summary>
    public class FluentValidationActionFilterTests
    {
        private sealed record Payload(string Name);

        private sealed class PayloadValidator : AbstractValidator<Payload>
        {
            public PayloadValidator() => RuleFor(payload => payload.Name).NotEmpty();
        }

        [Fact]
        public async Task AnInvalidArgument_FailsModelState_AndTheActionStillRuns()
        {
            var context = BuildContext(new Payload(""));
            var ran = false;

            await BuildFilter().OnActionExecutionAsync(context, () =>
            {
                ran = true;
                return Task.FromResult(new ActionExecutedContext(context, context.Filters, context.Controller));
            });

            Assert.False(context.ModelState.IsValid);
            Assert.True(ran);
        }

        [Fact]
        public async Task AValidArgument_LeavesModelStateAlone()
        {
            var context = BuildContext(new Payload("Rei"));

            await BuildFilter().OnActionExecutionAsync(context, () =>
                Task.FromResult(new ActionExecutedContext(context, context.Filters, context.Controller)));

            Assert.True(context.ModelState.IsValid);
        }

        [Fact]
        public async Task AnArgumentWithNoValidator_IsIgnored()
        {
            var context = BuildContext("no validator exists for a string");

            await BuildFilter().OnActionExecutionAsync(context, () =>
                Task.FromResult(new ActionExecutedContext(context, context.Filters, context.Controller)));

            Assert.True(context.ModelState.IsValid);
        }

        [Fact]
        public void TheFilter_RunsBeforeTheAutomatic400()
        {
            Assert.True(FluentValidationActionFilter.FilterOrder < -2000);
        }

        private static FluentValidationActionFilter BuildFilter()
        {
            var services = new ServiceCollection();
            services.AddTransient<IValidator<Payload>, PayloadValidator>();

            return new FluentValidationActionFilter(services.BuildServiceProvider());
        }

        private static ActionExecutingContext BuildContext(object argument) =>
            new(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor(), new ModelStateDictionary()),
                [],
                new Dictionary<string, object?> { ["payload"] = argument },
                controller: new object());
    }
}
