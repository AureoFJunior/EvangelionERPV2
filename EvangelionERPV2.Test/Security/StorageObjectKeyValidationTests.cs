using Amazon.S3;
using Amazon.S3.Model;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class StorageObjectKeyValidationTests
    {
        [Fact]
        public async Task GetItemBase64Async_WithUnsafeEmbeddedPayload_DoesNotCallS3()
        {
            var s3Client = new Mock<IAmazonS3>(MockBehavior.Strict);

            var result = await s3Client.Object.GetItemBase64Async(
                "test-bucket",
                "data:image/png;base64,aGVsbG8=");

            Assert.Empty(result);
            s3Client.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task DeleteItemIfExistsAsync_WithUnsafePathTraversalPayload_DoesNotCallS3()
        {
            var s3Client = new Mock<IAmazonS3>(MockBehavior.Strict);

            await s3Client.Object.DeleteItemIfExistsAsync("test-bucket", "../../etc/passwd");

            s3Client.VerifyNoOtherCalls();
        }

        [Fact]
        public void EnsureEncryptedAddress_WithUnsafeStorageAddress_ReturnsEmpty()
        {
            var result = SharedFunctions.EnsureEncryptedAddress("https://evil.example.com/object");

            Assert.Empty(result);
        }

        [Fact]
        public void EnsureDecryptedAddress_WithUnsafeStorageAddress_ReturnsEmpty()
        {
            var result = SharedFunctions.EnsureDecryptedAddress("data:text/plain;base64,aGVsbG8=");

            Assert.Empty(result);
        }

        [Fact]
        public void EnsureEncryptedAddress_WithAlreadyEncryptedAddress_ReturnsOriginalEncryptedValue()
        {
            EnsureEncryptionKeyInitialized();
            var encryptedAddress = SharedFunctions.Encrypt("products/example-file.jpg");

            var result = SharedFunctions.EnsureEncryptedAddress(encryptedAddress);

            Assert.Equal(encryptedAddress, result);
        }

        [Fact]
        public void EnsureEncryptedAddress_WithValidKeyAndMissingEncryptionKey_ReturnsPlainNormalizedKey()
        {
            var encryptionField = typeof(SharedFunctions)
                .GetField("_encryptionKey", BindingFlags.NonPublic | BindingFlags.Static);

            var originalValue = encryptionField?.GetValue(null);
            try
            {
                encryptionField?.SetValue(null, string.Empty);

                var result = SharedFunctions.EnsureEncryptedAddress("products/sample-image.jpg");

                Assert.Equal("products/sample-image.jpg", result);
            }
            finally
            {
                encryptionField?.SetValue(null, originalValue);
            }
        }

        [Fact]
        public void GetProductBucketName_WithLegacyAndNewConfigurationKeys_ResolvesConfiguredValue()
        {
            var legacyConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AWSSettings:BucketProducttName"] = "legacy-bucket"
                })
                .Build();

            var newConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AWSSettings:BucketProductName"] = "new-bucket"
                })
                .Build();

            Assert.Equal("legacy-bucket", SharedFunctions.GetProductBucketName(legacyConfiguration));
            Assert.Equal("new-bucket", SharedFunctions.GetProductBucketName(newConfiguration));
        }

        private static void EnsureEncryptionKeyInitialized()
        {
            var encryptionField = typeof(SharedFunctions)
                .GetField("_encryptionKey", BindingFlags.NonPublic | BindingFlags.Static);

            if (encryptionField == null)
                return;

            var currentValue = encryptionField.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(currentValue))
                return;

            var keyBytes = Enumerable.Range(1, 32).Select(index => (byte)index).ToArray();
            encryptionField.SetValue(null, Convert.ToBase64String(keyBytes));
        }
    }
}
