using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class ReportsControllerRequestSizeLimitAttributeTests
    {
        private const long ExpectedMaxBytes = 16L * 1024L;

        [Fact]
        public void ReportsController_GenerateReport_HasRequestSizeLimit()
        {
            var method = typeof(ReportsController).GetMethod(nameof(ReportsController.GenerateReport));

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
