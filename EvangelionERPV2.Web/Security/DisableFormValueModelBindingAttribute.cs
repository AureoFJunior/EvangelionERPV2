using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EvangelionERPV2.Web.Security
{
    /// <summary>
    /// Stops MVC from parsing the form on an action that reads the body itself.
    /// </summary>
    /// <remarks>
    /// MVC builds its value providers before an action runs, and the form ones read the whole
    /// multipart body whenever the request has a form content type — whether or not the action
    /// declares a single form parameter. Two consequences, both fatal to the voice endpoint: the
    /// body is already drained by the time a <c>MultipartReader</c> gets it, and the file was
    /// spooled to a temp file on the way, which is precisely the recording this feature promises
    /// never to write down. Removing the factories is the documented way to own the body.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            var factories = context.ValueProviderFactories;
            factories.RemoveType<FormValueProviderFactory>();
            factories.RemoveType<FormFileValueProviderFactory>();
            factories.RemoveType<JQueryFormValueProviderFactory>();
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}
