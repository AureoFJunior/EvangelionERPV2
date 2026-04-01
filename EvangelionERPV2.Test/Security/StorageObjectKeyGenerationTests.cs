using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.Test.Security
{
    public class StorageObjectKeyGenerationTests
    {
        [Fact]
        public void GenerateStorageObjectKey_WithEntityId_IncludesPrefixAndIdSegment()
        {
            var entityId = Guid.NewGuid();

            var key = SharedFunctions.GenerateStorageObjectKey("Users", entityId);

            Assert.StartsWith("users/", key);
            Assert.Contains($"{entityId:N}-", key);
            Assert.Matches(@"^[a-z0-9/_-]+$", key);
        }

        [Fact]
        public void GenerateStorageObjectKey_WithUnsafePrefix_FallsBackToFilesPrefix()
        {
            var key = SharedFunctions.GenerateStorageObjectKey("$$$", Guid.Empty);

            Assert.StartsWith("files/", key);
        }

        [Fact]
        public void GenerateStorageObjectKey_WhenCalledTwice_ReturnsDifferentKeys()
        {
            var entityId = Guid.NewGuid();

            var firstKey = SharedFunctions.GenerateStorageObjectKey("products", entityId);
            var secondKey = SharedFunctions.GenerateStorageObjectKey("products", entityId);

            Assert.NotEqual(firstKey, secondKey);
        }
    }
}
