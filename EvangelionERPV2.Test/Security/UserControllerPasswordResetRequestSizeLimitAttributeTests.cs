using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class UserControllerPasswordResetRequestSizeLimitAttributeTests
    {
        private const long ExpectedMaxBytes = 16L * 1024L;

        [Fact]
        public void UserController_RequestPasswordReset_HasRequestSizeLimit()
        {
            var method = typeof(UserController).GetMethod(nameof(UserController.RequestPasswordReset));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(ExpectedMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
        }

        [Fact]
        public void UserController_ResetPassword_HasRequestSizeLimit()
        {
            var method = typeof(UserController).GetMethod(nameof(UserController.ResetPassword));

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
