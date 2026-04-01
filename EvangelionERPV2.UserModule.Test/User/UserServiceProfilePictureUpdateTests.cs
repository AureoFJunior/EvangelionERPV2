using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Reflection;

namespace EvangelionERPV2.UserModule.Test.User
{
    public class UserServiceProfilePictureUpdateTests
    {
        [Fact]
        public async Task UpdateProfilePictureAsync_WithNewPayload_DeletesOldPictureAfterDatabaseUpdate()
        {
            EnsureEncryptionKeyInitialized();

            var (service, userRepository, s3ClientMock) = CreateService();
            var userId = Guid.NewGuid();
            var existingUser = BuildUser(userId, "users/old-picture-key");
            var updateUser = BuildUser(userId, existingUser.ProfilePicture);
            var payload = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 });

            var events = new List<string>();
            var getByIdCalls = 0;

            userRepository
                .Setup(r => r.GetById(userId))
                .Returns(() =>
                {
                    getByIdCalls++;
                    events.Add(getByIdCalls == 1 ? "get-initial" : "get-update");
                    return existingUser;
                });

            userRepository
                .Setup(r => r.Update(It.IsAny<Shared.Entities.User>()))
                .Callback(() => events.Add("update"))
                .Returns<Shared.Entities.User>(entity => entity);

            userRepository
                .Setup(r => r.Commit(It.IsAny<CancellationToken>()))
                .Callback(() => events.Add("commit"));

            s3ClientMock
                .Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
                .Callback(() => events.Add("upload-new"))
                .ReturnsAsync(new PutObjectResponse());

            s3ClientMock
                .Setup(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
                .Callback<DeleteObjectRequest, CancellationToken>((request, _) =>
                {
                    events.Add(request.Key == "users/old-picture-key" ? "delete-old" : "delete-other");
                })
                .ReturnsAsync(new DeleteObjectResponse());

            var result = await service.UpdateProfilePictureAsync(updateUser, payload);

            Assert.NotNull(result);
            Assert.Equal(
                ["get-initial", "upload-new", "get-update", "update", "commit", "delete-old"],
                events);
        }

        [Fact]
        public async Task UpdateProfilePictureAsync_WhenDatabaseUpdateFails_DeletesOnlyNewUploadedPicture()
        {
            EnsureEncryptionKeyInitialized();

            var (service, userRepository, s3ClientMock) = CreateService();
            var userId = Guid.NewGuid();
            var existingUser = BuildUser(userId, "users/old-picture-key");
            var updateUser = BuildUser(userId, existingUser.ProfilePicture);
            var payload = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D });

            var deletedKeys = new List<string>();
            string? uploadedKey = null;

            userRepository.Setup(r => r.GetById(userId)).Returns(existingUser);
            userRepository
                .Setup(r => r.Update(It.IsAny<Shared.Entities.User>()))
                .Throws(new InvalidOperationException("db update failure"));

            s3ClientMock
                .Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
                .Callback<PutObjectRequest, CancellationToken>((request, _) => uploadedKey = request.Key)
                .ReturnsAsync(new PutObjectResponse());

            s3ClientMock
                .Setup(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
                .Callback<DeleteObjectRequest, CancellationToken>((request, _) => deletedKeys.Add(request.Key))
                .ReturnsAsync(new DeleteObjectResponse());

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateProfilePictureAsync(updateUser, payload));

            Assert.False(string.IsNullOrWhiteSpace(uploadedKey));
            Assert.Single(deletedKeys);
            Assert.Equal(uploadedKey, deletedKeys[0]);
            Assert.DoesNotContain("users/old-picture-key", deletedKeys);
        }

        [Fact]
        public async Task UpdateProfilePictureAsync_WhenOldPictureDeletionFails_DoesNotFailSuccessfulUpdate()
        {
            EnsureEncryptionKeyInitialized();

            var (service, userRepository, s3ClientMock) = CreateService();
            var userId = Guid.NewGuid();
            var existingUser = BuildUser(userId, "users/old-picture-key");
            var updateUser = BuildUser(userId, existingUser.ProfilePicture);
            var payload = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 });

            var getByIdCalls = 0;
            userRepository
                .Setup(r => r.GetById(userId))
                .Returns(() =>
                {
                    getByIdCalls++;
                    return existingUser;
                });

            userRepository
                .Setup(r => r.Update(It.IsAny<Shared.Entities.User>()))
                .Returns<Shared.Entities.User>(entity => entity);

            userRepository
                .Setup(r => r.Commit(It.IsAny<CancellationToken>()));

            s3ClientMock
                .Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PutObjectResponse());

            s3ClientMock
                .Setup(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
                .Returns<DeleteObjectRequest, CancellationToken>((request, _) =>
                {
                    if (request.Key == "users/old-picture-key")
                        throw new InvalidOperationException("s3 delete old failed");

                    return Task.FromResult(new DeleteObjectResponse());
                });

            var result = await service.UpdateProfilePictureAsync(updateUser, payload);

            Assert.NotNull(result);
            userRepository.Verify(r => r.Update(It.IsAny<Shared.Entities.User>()), Times.Once);
            userRepository.Verify(r => r.Commit(It.IsAny<CancellationToken>()), Times.Once);
            s3ClientMock.Verify(
                s => s.DeleteObjectAsync(
                    It.Is<DeleteObjectRequest>(request => request.Key == "users/old-picture-key"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private static (UserService service,
            Mock<IRepository<Shared.Entities.User>> userRepository,
            Mock<IAmazonS3> s3ClientMock) CreateService()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AWSSettings:SecretName"] = "test-secret",
                    ["AWSSettings:BucketUserName"] = "test-user-bucket"
                })
                .Build();

            var secretsManager = new Mock<IAmazonSecretsManager>();
            secretsManager.Setup(s => s.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse
                {
                    SecretString = "{\"access-key-id\":\"test\",\"secret-access-key\":\"test\"}"
                });

            var kmsProvider = new AWSKMSKeyProvider(secretsManager.Object, configuration);
            var userRepository = new Mock<IRepository<Shared.Entities.User>>();
            var refreshTokenRepository = new Mock<IRepository<RefreshToken>>();
            var passwordResetTokenRepository = new Mock<IRepository<PasswordResetToken>>();
            var s3ClientMock = new Mock<IAmazonS3>();

            var service = new UserService(
                userRepository.Object,
                refreshTokenRepository.Object,
                passwordResetTokenRepository.Object,
                configuration,
                kmsProvider);

            typeof(UserService)
                .GetField("_s3Client", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(service, s3ClientMock.Object);

            return (service, userRepository, s3ClientMock);
        }

        private static Shared.Entities.User BuildUser(Guid userId, string profilePicture)
        {
            return new Shared.Entities.User
            {
                Id = userId,
                UserName = "user.test",
                FirstName = "User",
                LastName = "Test",
                Email = "user@evangelion.com",
                BirthDate = DateTime.UtcNow.AddYears(-25),
                IsActive = true,
                ProfilePicture = profilePicture
            };
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

            var keyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
            encryptionField.SetValue(null, Convert.ToBase64String(keyBytes));
        }
    }
}
