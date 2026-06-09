using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.Test.Security
{
    public class Base64PayloadStreamValidationTests
    {
        [Fact]
        public void GetMemoryStreamFromBase64Payload_WhenDecodedPayloadExceedsDefaultLimit_Throws()
        {
            var payload = Convert.ToBase64String(new byte[(5 * 1024 * 1024) + 1]);

            var exception = Assert.Throws<ArgumentException>(() =>
                SharedFunctions.GetMemoryStreamFromBase64Payload(payload));

            Assert.Contains("bytes or smaller after decoding", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void GetMemoryStreamFromBase64Payload_WhenPayloadIsValid_ReturnsStream()
        {
            var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("hello"));

            using var stream = SharedFunctions.GetMemoryStreamFromBase64Payload(payload);

            Assert.NotNull(stream);
            Assert.Equal(5, stream.Length);
        }
    }
}
