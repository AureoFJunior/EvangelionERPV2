using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class UploadRequestSizeLimitAttributeTests
    {
        private const long ExpectedMaxBytes = 8L * 1024L * 1024L;

        [Fact]
        public void ProductController_AddProduct_HasRequestSizeLimit()
        {
            var method = typeof(ProductController).GetMethod(nameof(ProductController.AddProduct));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(ExpectedMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
        }

        [Fact]
        public void ProductController_UploadPicture_HasRequestSizeLimit()
        {
            var method = typeof(ProductController).GetMethod(nameof(ProductController.UploadPicture));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(ExpectedMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
        }

        [Fact]
        public void ProductController_UpdateProduct_HasRequestSizeLimit()
        {
            var method = typeof(ProductController).GetMethod(nameof(ProductController.UpdateProduct));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(ExpectedMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
        }

        [Fact]
        public void UserController_UpdateProfilePicture_HasRequestSizeLimit()
        {
            var method = typeof(UserController).GetMethod(nameof(UserController.UpdateProfilePicture));

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
                .Where(attribute => attribute.AttributeType == typeof(RequestSizeLimitAttribute))
                .FirstOrDefault();
        }
    }
}
