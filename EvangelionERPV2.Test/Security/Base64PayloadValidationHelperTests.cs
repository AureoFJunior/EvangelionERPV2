using EvangelionERPV2.Web.FluentValidator;

namespace EvangelionERPV2.Test.Security
{
    public class Base64PayloadValidationHelperTests
    {
        [Fact]
        public void IsValidBase64Payload_WithDataUriPayload_ReturnsTrue()
        {
            const string payload = "data:image/png;base64,aGVsbG8=";

            var isValid = Base64PayloadValidationHelper.IsValidBase64Payload(payload);

            Assert.True(isValid);
        }

        [Fact]
        public void IsValidBase64Payload_WithInvalidCharacters_ReturnsFalse()
        {
            const string payload = "aGVsbG8$";

            var isValid = Base64PayloadValidationHelper.IsValidBase64Payload(payload);

            Assert.False(isValid);
        }

        [Fact]
        public void IsWithinDecodedSizeLimit_WhenPayloadExceedsLimit_ReturnsFalse()
        {
            var bytes = Enumerable.Repeat((byte)1, 2048).ToArray();
            var payload = Convert.ToBase64String(bytes);

            var withinLimit = Base64PayloadValidationHelper.IsWithinDecodedSizeLimit(payload, 1024);

            Assert.False(withinLimit);
        }

        [Fact]
        public void HasSupportedImageSignature_WithPngPayload_ReturnsTrue()
        {
            byte[] pngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
            var payload = Convert.ToBase64String(pngBytes);

            var hasSignature = Base64PayloadValidationHelper.HasSupportedImageSignature(payload);

            Assert.True(hasSignature);
        }

        [Fact]
        public void HasSupportedImageSignature_WithDataUriJpegPayload_ReturnsTrue()
        {
            byte[] jpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
            var payload = $"data:image/jpeg;base64,{Convert.ToBase64String(jpegBytes)}";

            var hasSignature = Base64PayloadValidationHelper.HasSupportedImageSignature(payload);

            Assert.True(hasSignature);
        }

        [Fact]
        public void HasSupportedImageSignature_WithNonImagePayload_ReturnsFalse()
        {
            var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not-an-image"));

            var hasSignature = Base64PayloadValidationHelper.HasSupportedImageSignature(payload);

            Assert.False(hasSignature);
        }
    }
}
