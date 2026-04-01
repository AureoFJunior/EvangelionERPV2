using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class EnterpriseControllerRequestSizeLimitAttributeTests
    {
        private const long ExpectedMaxBytes = 64L * 1024L;

        [Fact]
        public void EnterpriseController_AddEnterprise_HasRequestSizeLimit()
        {
            var method = typeof(EnterpriseController).GetMethod(nameof(EnterpriseController.AddEnterprise));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(ExpectedMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
        }

        [Fact]
        public void EnterpriseController_UpdateEnterprise_HasRequestSizeLimit()
        {
            var method = typeof(EnterpriseController).GetMethod(nameof(EnterpriseController.UpdateEnterprise));

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
