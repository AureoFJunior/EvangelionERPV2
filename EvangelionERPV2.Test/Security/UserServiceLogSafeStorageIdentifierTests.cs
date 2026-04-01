using EvangelionERPV2.UserModule.Application.Services;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class UserServiceLogSafeStorageIdentifierTests
    {
        [Fact]
        public void GetLogSafeStorageIdentifier_DoesNotExposeRawKey_AndIsDeterministic()
        {
            var hashA = InvokeGetLogSafeStorageIdentifier("  users/private/profile-picture.png  ");
            var hashB = InvokeGetLogSafeStorageIdentifier("users/private/profile-picture.png");

            Assert.Equal(12, hashA.Length);
            Assert.Matches("^[a-f0-9]{12}$", hashA);
            Assert.Equal(hashA, hashB);
            Assert.DoesNotContain("profile-picture.png", hashA, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetLogSafeStorageIdentifier_WhenEmpty_ReturnsEmptyMarker()
        {
            var hash = InvokeGetLogSafeStorageIdentifier("   ");
            Assert.Equal("empty", hash);
        }

        private static string InvokeGetLogSafeStorageIdentifier(string? storageObjectKey)
        {
            var method = typeof(UserService).GetMethod("GetLogSafeStorageIdentifier", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var result = method.Invoke(null, new object?[] { storageObjectKey });
            return Assert.IsType<string>(result);
        }
    }
}
