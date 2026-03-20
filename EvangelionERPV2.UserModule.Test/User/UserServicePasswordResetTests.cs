using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace EvangelionERPV2.UserModule.Test.User
{
    public class UserServicePasswordResetTests
    {
        [Fact]
        public async Task CreatePasswordResetTokenAsync_WhenUserDoesNotExist_ReturnsNull()
        {
            var (service, userRepository, _, passwordResetTokenRepository) = CreateService();
            userRepository.Setup(r => r.GetByCondition(It.IsAny<Func<Shared.Entities.User, bool>>()))
                .Returns([]);

            var token = await service.CreatePasswordResetTokenAsync("missing@evangelion.com");

            Assert.Null(token);
            passwordResetTokenRepository.Verify(r => r.CreateAsync(It.IsAny<PasswordResetToken>()), Times.Never);
        }

        [Fact]
        public async Task CreatePasswordResetTokenAsync_WhenUserExists_InvalidatesActiveTokensAndCreatesNew()
        {
            var (service, userRepository, _, passwordResetTokenRepository) = CreateService();
            var user = BuildUser();
            var activeToken = new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = "old-hash",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                IsActive = true
            };

            userRepository.Setup(r => r.GetByCondition(It.IsAny<Func<Shared.Entities.User, bool>>()))
                .Returns([user]);
            passwordResetTokenRepository.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<Task>, CancellationToken>(async (operation, _) => await operation());
            passwordResetTokenRepository.Setup(r => r.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<PasswordResetToken, bool>>>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<PasswordResetToken, object>>>()))
                .ReturnsAsync([activeToken]);
            passwordResetTokenRepository.Setup(r => r.Update(It.IsAny<PasswordResetToken>()))
                .Returns<PasswordResetToken>(entity => entity);
            passwordResetTokenRepository.Setup(r => r.CreateAsync(It.IsAny<PasswordResetToken>()))
                .ReturnsAsync((PasswordResetToken entity) => entity);
            passwordResetTokenRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var token = await service.CreatePasswordResetTokenAsync(user.Email);

            Assert.False(string.IsNullOrWhiteSpace(token));
            Assert.Matches(new Regex(@"^\d{8}$"), token!);
            Assert.False(activeToken.IsActive ?? true);
            passwordResetTokenRepository.Verify(r => r.Update(It.IsAny<PasswordResetToken>()), Times.AtLeastOnce);
            passwordResetTokenRepository.Verify(r => r.CreateAsync(It.IsAny<PasswordResetToken>()), Times.Once);
            passwordResetTokenRepository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenPasswordTooShort_ThrowsArgumentException()
        {
            var (service, _, _, _) = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.ResetPasswordAsync("user@evangelion.com", "token", "123"));
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenPasswordHasNoDigit_ThrowsArgumentException()
        {
            var (service, _, _, _) = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.ResetPasswordAsync("user@evangelion.com", "token", "Password"));
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenPasswordHasNoUppercaseOrSpecial_ThrowsArgumentException()
        {
            var (service, _, _, _) = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.ResetPasswordAsync("user@evangelion.com", "token", "password1"));
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenTokenInvalid_ThrowsArgumentException()
        {
            var (service, userRepository, _, passwordResetTokenRepository) = CreateService();
            var user = BuildUser();
            var tokenEntity = new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = ComputeSha256("12345678"),
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                IsActive = true
            };
            userRepository.Setup(r => r.GetByCondition(It.IsAny<Func<Shared.Entities.User, bool>>()))
                .Returns([user]);
            passwordResetTokenRepository.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<Task>, CancellationToken>(async (operation, _) => await operation());
            passwordResetTokenRepository.Setup(r => r.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<PasswordResetToken, bool>>>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<PasswordResetToken, object>>>()))
                .ReturnsAsync([tokenEntity]);
            passwordResetTokenRepository.Setup(r => r.Update(It.IsAny<PasswordResetToken>()))
                .Returns<PasswordResetToken>(entity => entity);
            passwordResetTokenRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.ResetPasswordAsync(user.Email, "12345679", "Newpassword1"));

            Assert.Equal(1, tokenEntity.FailedAttempts);
            Assert.True(tokenEntity.IsActive);
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenValid_UpdatesPasswordAndRevokesRefreshTokens()
        {
            var (service, userRepository, refreshTokenRepository, passwordResetTokenRepository) = CreateService();
            var user = BuildUser();
            user.Password = SharedFunctions.HashPassword("oldpassword");
            var resetToken = new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = ComputeSha256("12345678"),
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                FailedAttempts = 0,
                IsActive = true
            };
            var refreshToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = "refresh",
                ExpiresAt = DateTime.UtcNow.AddDays(1),
                IsActive = true
            };

            userRepository.Setup(r => r.GetByCondition(It.IsAny<Func<Shared.Entities.User, bool>>()))
                .Returns([user]);
            userRepository.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<Task>, CancellationToken>(async (operation, _) => await operation());
            userRepository.Setup(r => r.Update(It.IsAny<Shared.Entities.User>()))
                .Returns<Shared.Entities.User>(entity => entity);
            userRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            passwordResetTokenRepository.Setup(r => r.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<PasswordResetToken, bool>>>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<PasswordResetToken, object>>>()))
                .ReturnsAsync([resetToken]);
            passwordResetTokenRepository.Setup(r => r.Update(It.IsAny<PasswordResetToken>()))
                .Returns<PasswordResetToken>(entity => entity);

            refreshTokenRepository.Setup(r => r.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<RefreshToken, bool>>>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<RefreshToken, object>>>()))
                .ReturnsAsync([refreshToken]);
            refreshTokenRepository.Setup(r => r.Update(It.IsAny<RefreshToken>()))
                .Returns<RefreshToken>(entity => entity);

            await service.ResetPasswordAsync(user.Email, "12345678", "Newpassword1");

            Assert.True(SharedFunctions.VerifyPassword("Newpassword1", user.Password, out _));
            Assert.False(resetToken.IsActive ?? true);
            Assert.NotNull(resetToken.UsedAt);
            Assert.NotNull(refreshToken.RevokedAt);
            refreshTokenRepository.Verify(r => r.Update(It.IsAny<RefreshToken>()), Times.AtLeastOnce);
            userRepository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenFailedAttemptsReachLimit_DeactivatesToken()
        {
            var (service, userRepository, _, passwordResetTokenRepository) = CreateService();
            var user = BuildUser();
            var tokenEntity = new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = ComputeSha256("12345678"),
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                FailedAttempts = 4,
                IsActive = true
            };

            userRepository.Setup(r => r.GetByCondition(It.IsAny<Func<Shared.Entities.User, bool>>()))
                .Returns([user]);
            passwordResetTokenRepository.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<Task>, CancellationToken>(async (operation, _) => await operation());
            passwordResetTokenRepository.Setup(r => r.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<PasswordResetToken, bool>>>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<PasswordResetToken, object>>>()))
                .ReturnsAsync([tokenEntity]);
            passwordResetTokenRepository.Setup(r => r.Update(It.IsAny<PasswordResetToken>()))
                .Returns<PasswordResetToken>(entity => entity);
            passwordResetTokenRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.ResetPasswordAsync(user.Email, "12345679", "Newpassword1"));

            Assert.Equal(5, tokenEntity.FailedAttempts);
            Assert.False(tokenEntity.IsActive ?? true);
        }

        private static (UserService service,
            Mock<IRepository<Shared.Entities.User>> userRepository,
            Mock<IRepository<RefreshToken>> refreshTokenRepository,
            Mock<IRepository<PasswordResetToken>> passwordResetTokenRepository) CreateService()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AWSSettings:SecretName"] = "test-secret"
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

            var service = new UserService(
                userRepository.Object,
                refreshTokenRepository.Object,
                passwordResetTokenRepository.Object,
                configuration,
                kmsProvider);

            return (service, userRepository, refreshTokenRepository, passwordResetTokenRepository);
        }

        private static Shared.Entities.User BuildUser()
        {
            return new Shared.Entities.User
            {
                Id = Guid.NewGuid(),
                Email = "user@evangelion.com",
                FirstName = "User",
                LastName = "Test",
                UserName = "user.test",
                Password = SharedFunctions.HashPassword("password123"),
                BirthDate = DateTime.UtcNow.AddYears(-20),
                IsActive = true
            };
        }

        private static string ComputeSha256(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes);
        }
    }
}
