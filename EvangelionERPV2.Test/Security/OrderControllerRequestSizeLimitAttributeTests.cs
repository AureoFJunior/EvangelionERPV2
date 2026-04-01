using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class OrderControllerRequestSizeLimitAttributeTests
    {
        private const long ExpectedMaxBytes = 1L * 1024L * 1024L;

        [Fact]
        public void OrderController_AddOrder_HasRequestSizeLimit()
        {
            var method = typeof(OrderController).GetMethod(nameof(OrderController.AddOrder));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(ExpectedMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
        }

        [Fact]
        public void OrderController_InsertOrder_HasRequestSizeLimit()
        {
            var method = typeof(OrderController).GetMethod(nameof(OrderController.InsertOrder));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(ExpectedMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
        }

        [Fact]
        public void OrderController_UpdateOrder_HasRequestSizeLimit()
        {
            var method = typeof(OrderController).GetMethod(nameof(OrderController.UpdateOrder));

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
