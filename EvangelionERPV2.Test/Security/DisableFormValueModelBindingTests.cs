using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The voice endpoint reads the request body itself. MVC would otherwise parse the form first —
    /// draining the body before the action sees it and spooling the clip to a temp file on the way,
    /// which is exactly the recording the feature promises never to write down.
    /// </summary>
    public class DisableFormValueModelBindingTests
    {
        [Fact]
        public void ItRemovesEveryFormValueProvider()
        {
            var context = Executing(new List<IValueProviderFactory>
            {
                new QueryStringValueProviderFactory(),
                new FormValueProviderFactory(),
                new FormFileValueProviderFactory(),
                new JQueryFormValueProviderFactory(),
                new RouteValueProviderFactory(),
            });

            new DisableFormValueModelBindingAttribute().OnResourceExecuting(context);

            Assert.DoesNotContain(context.ValueProviderFactories, factory => factory is FormValueProviderFactory);
            Assert.DoesNotContain(context.ValueProviderFactories, factory => factory is FormFileValueProviderFactory);
            Assert.DoesNotContain(context.ValueProviderFactories, factory => factory is JQueryFormValueProviderFactory);
        }

        [Fact]
        public void ItLeavesTheProvidersThatDoNotTouchTheBody()
        {
            var context = Executing(new List<IValueProviderFactory>
            {
                new QueryStringValueProviderFactory(),
                new FormValueProviderFactory(),
                new RouteValueProviderFactory(),
            });

            new DisableFormValueModelBindingAttribute().OnResourceExecuting(context);

            Assert.Contains(context.ValueProviderFactories, factory => factory is QueryStringValueProviderFactory);
            Assert.Contains(context.ValueProviderFactories, factory => factory is RouteValueProviderFactory);
        }

        private static ResourceExecutingContext Executing(IList<IValueProviderFactory> factories) =>
            new(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                factories);
    }
}
