using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class PayableBillsControllerRequestSizeLimitAttributeTests
    {
        private const long ExpectedMaxBytes = 1L * 1024L * 1024L;

        [Fact]
        public void PayableBillsController_AddPayableBill_HasRequestSizeLimit()
        {
            var method = typeof(PayableBillsController).GetMethod(nameof(PayableBillsController.AddPayableBill));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(ExpectedMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
        }

        [Fact]
        public void PayableBillsController_UpdatePayableBill_HasRequestSizeLimit()
        {
            var method = typeof(PayableBillsController).GetMethod(nameof(PayableBillsController.UpdatePayableBill));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(ExpectedMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
        }

        [Fact]
        public void PayableBillsController_GetReplenishmentSuggestions_HasRequestSizeLimit()
        {
            var method = typeof(PayableBillsController).GetMethod(nameof(PayableBillsController.GetReplenishmentSuggestions));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(ExpectedMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
        }

        [Fact]
        public void PayableBillsController_RefundPayableBill_HasRequestSizeLimit()
        {
            var method = typeof(PayableBillsController).GetMethod(nameof(PayableBillsController.RefundPayableBill));

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
