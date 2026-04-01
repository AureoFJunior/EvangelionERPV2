using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class CashFlowForecastControllerRequestSizeLimitAttributeTests
    {
        private const long ExpectedMaxBytes = 128L * 1024L;

        [Fact]
        public void CashFlowForecastController_RunSimulation_HasRequestSizeLimit()
        {
            var method = typeof(CashFlowForecastController).GetMethod(nameof(CashFlowForecastController.RunSimulation));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(ExpectedMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
        }

        private static CustomAttributeData? GetRequestSizeLimitAttributeData(MethodInfo? method)
        {
            if (method == null)
                return null;

            return method
                .CustomAttributes
                .FirstOrDefault(attribute => attribute.AttributeType == typeof(RequestSizeLimitAttribute));
        }
    }
}
