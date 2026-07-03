using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class UserControllerAnonymousLoginRequestSizeLimitAttributeTests
    {
        private const long ExpectedMaxBytes = 32L * 1024L;

        [Fact]
        public void UserController_LogInto_HasRequestSizeLimit()
        {
            var method = typeof(UserController).GetMethod(nameof(UserController.LogInto));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(ExpectedMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
        }

        [Fact]
        public void UserController_LoginWithGoogle_HasRequestSizeLimit()
        {
            var method = typeof(UserController).GetMethod(nameof(UserController.LoginWithGoogle));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(ExpectedMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
        }

        [Fact]
        public void UserController_LoginWithGoogleCode_HasRequestSizeLimit()
        {
            var method = typeof(UserController).GetMethod(nameof(UserController.LoginWithGoogleCode));

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
