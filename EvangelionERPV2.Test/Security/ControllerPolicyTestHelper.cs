using System.Reflection;
using Microsoft.AspNetCore.Authorization;

namespace EvangelionERPV2.Test.Security
{
    internal static class ControllerPolicyTestHelper
    {
        public static void AssertActionPolicy<TController>(string actionName, string expectedPolicy)
        {
            var method = typeof(TController)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(x => x.Name == actionName);

            var actionPolicy = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .FirstOrDefault(x => x.Policy == expectedPolicy);
            var controllerPolicy = typeof(TController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .FirstOrDefault(x => x.Policy == expectedPolicy);

            Assert.True(
                actionPolicy != null || controllerPolicy != null,
                $"Expected {typeof(TController).Name}.{actionName} to require policy '{expectedPolicy}'.");
        }
    }
}
