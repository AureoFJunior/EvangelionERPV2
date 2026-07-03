using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class EmailControllerRequestSizeLimitAttributeTests
    {
        [Fact]
        public void EmailController_AddEmail_HasRequestSizeLimit()
        {
            const long expectedAddEmailMaxBytes = 64L * 1024L;
            var method = typeof(EmailController).GetMethod(nameof(EmailController.AddEmail));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(expectedAddEmailMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
        }

        [Fact]
        public void EmailController_SendQueuedEmail_HasLargerQueuedDeliveryLimit()
        {
            // Queued delivery replays full signed MIME reports, so it must not reuse the
            // small 64 KiB email-settings limit.
            const long expectedQueuedEmailMaxBytes = 4L * 1024L * 1024L;
            var method = typeof(EmailController).GetMethod(nameof(EmailController.SendQueuedEmail));

            var attributeData = GetRequestSizeLimitAttributeData(method);

            Assert.NotNull(attributeData);
            Assert.Single(attributeData!.ConstructorArguments);
            Assert.Equal(expectedQueuedEmailMaxBytes, (long)attributeData.ConstructorArguments[0].Value!);
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
