using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Text.RegularExpressions;
using Xunit;

namespace EvangelionERPV2.UserModule.Test.User
{
    public class UserServicePasswordResetTests
    {
        [Fact]
        public async Task CreatePasswordResetTokenAsync_WhenUserDoesNotExist_ReturnsNull()
        {
            var (service, userRepository, enterpriseRepository, _, passwordResetTokenRepository) = CreateService();
            enterpriseRepository
                .Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
                .Returns<Guid>(_ => Task.FromResult<Enterprise>(null!));
            SetupUniqueUserLookup(userRepository, null);

            var token = await service.CreatePasswordResetTokenAsync("missing@evangelion.com");

            Assert.Null(token);
            passwordResetTokenRepository.Verify(r => r.CreateAsync(It.IsAny<PasswordResetToken>()), Times.Never);
        }

        [Fact]
        public async Task CreatePasswordResetTokenAsync_WhenUserExists_InvalidatesActiveTokensAndCreatesNew()
        {
            var (service, userRepository, enterpriseRepository, _, passwordResetTokenRepository) = CreateService();
            var user = BuildUser();
            enterpriseRepository.Setup(r => r.GetByIdAsync(user.EnterpriseId!.Value))
                .ReturnsAsync(new Enterprise { Id = user.EnterpriseId!.Value, IsActive = true });
            var activeToken = new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = "old-hash",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                IsActive = true
            };

            SetupUniqueUserLookup(userRepository, user);
            passwordResetTokenRepository.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<Task>, CancellationToken>(async (operation, _) => await operation());
            passwordResetTokenRepository.Setup(r => r.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<PasswordResetToken, bool>>>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<PasswordResetToken, object>>>()))
                .ReturnsAsync([activeToken]);
            var persistenceEvents = new List<string>();
            passwordResetTokenRepository.Setup(r => r.Update(It.IsAny<PasswordResetToken>()))
                .Callback(() => persistenceEvents.Add("update"))
                .Returns<PasswordResetToken>(entity => entity);
            passwordResetTokenRepository.Setup(r => r.CreateAsync(It.IsAny<PasswordResetToken>()))
                .Callback(() => persistenceEvents.Add("create"))
                .ReturnsAsync((PasswordResetToken entity) => entity);
            passwordResetTokenRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Callback(() => persistenceEvents.Add("commit"))
                .Returns(Task.CompletedTask);

            var token = await service.CreatePasswordResetTokenAsync(user.Email);

            Assert.False(string.IsNullOrWhiteSpace(token));
            Assert.Matches(new Regex(@"^\d{8}$"), token!);
            Assert.False(activeToken.IsActive ?? true);
            Assert.Equal(["update", "commit", "create", "commit"], persistenceEvents);
            passwordResetTokenRepository.Verify(r => r.Update(It.IsAny<PasswordResetToken>()), Times.AtLeastOnce);
            passwordResetTokenRepository.Verify(r => r.CreateAsync(It.IsAny<PasswordResetToken>()), Times.Once);
            passwordResetTokenRepository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task CreatePasswordResetTokenAsync_WhenExistingActiveTokenIsExpired_DeactivatesItBeforeInsert()
        {
            var (service, userRepository, enterpriseRepository, _, passwordResetTokenRepository) = CreateService();
            var user = BuildUser();
            enterpriseRepository.Setup(r => r.GetByIdAsync(user.EnterpriseId!.Value))
                .ReturnsAsync(new Enterprise { Id = user.EnterpriseId!.Value, IsActive = true });
            var expiredActiveToken = new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = "expired-hash",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
                IsActive = true
            };

            SetupUniqueUserLookup(userRepository, user);
            passwordResetTokenRepository.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<Task>, CancellationToken>(async (operation, _) => await operation());
            passwordResetTokenRepository.Setup(r => r.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<PasswordResetToken, bool>>>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<PasswordResetToken, object>>>()))
                .ReturnsAsync((
                    bool _,
                    int? _,
                    int? _,
                    System.Linq.Expressions.Expression<Func<PasswordResetToken, bool>> predicate,
                    System.Linq.Expressions.Expression<Func<PasswordResetToken, object>> _) =>
                        new[] { expiredActiveToken }.Where(predicate.Compile()).ToList());
            var persistenceEvents = new List<string>();
            passwordResetTokenRepository.Setup(r => r.Update(It.IsAny<PasswordResetToken>()))
                .Callback(() => persistenceEvents.Add("update"))
                .Returns<PasswordResetToken>(entity => entity);
            passwordResetTokenRepository.Setup(r => r.CreateAsync(It.IsAny<PasswordResetToken>()))
                .Callback(() => persistenceEvents.Add("create"))
                .ReturnsAsync((PasswordResetToken entity) => entity);
            passwordResetTokenRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Callback(() => persistenceEvents.Add("commit"))
                .Returns(Task.CompletedTask);

            var token = await service.CreatePasswordResetTokenAsync(user.Email);

            Assert.False(string.IsNullOrWhiteSpace(token));
            Assert.False(expiredActiveToken.IsActive ?? true);
            Assert.Equal(["update", "commit", "create", "commit"], persistenceEvents);
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenPasswordTooShort_ThrowsArgumentException()
        {
            var (service, _, _, _, _) = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.ResetPasswordAsync("user@evangelion.com", "token", "123"));
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenPasswordHasNoDigit_ThrowsArgumentException()
        {
            var (service, _, _, _, _) = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.ResetPasswordAsync("user@evangelion.com", "token", "Password"));
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenPasswordHasNoUppercaseOrSpecial_ThrowsArgumentException()
        {
            var (service, _, _, _, _) = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.ResetPasswordAsync("user@evangelion.com", "token", "password1"));
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenTokenInvalid_ThrowsArgumentException()
        {
            var (service, userRepository, _, _, passwordResetTokenRepository) = CreateService();
            var user = BuildUser();
            var tokenEntity = new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = ComputePasswordResetTokenHash(service, "12345678"),
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                IsActive = true
            };
            SetupUniqueUserLookup(userRepository, user);
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
            var (service, userRepository, _, refreshTokenRepository, passwordResetTokenRepository) = CreateService();
            var user = BuildUser();
            user.Password = SharedFunctions.HashPassword("oldpassword");
            var resetToken = new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = ComputePasswordResetTokenHash(service, "12345678"),
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

            SetupUniqueUserLookup(userRepository, user);
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
            var (service, userRepository, _, _, passwordResetTokenRepository) = CreateService();
            var user = BuildUser();
            var tokenEntity = new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = ComputePasswordResetTokenHash(service, "12345678"),
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                FailedAttempts = 4,
                IsActive = true
            };

            SetupUniqueUserLookup(userRepository, user);
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
            Mock<IRepository<Enterprise>> enterpriseRepository,
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
            var enterpriseRepository = new Mock<IRepository<Enterprise>>();
            var refreshTokenRepository = new Mock<IRepository<RefreshToken>>();
            var passwordResetTokenRepository = new Mock<IRepository<PasswordResetToken>>();

            var service = new UserService(
                userRepository.Object,
                enterpriseRepository.Object,
                refreshTokenRepository.Object,
                passwordResetTokenRepository.Object,
                configuration,
                kmsProvider);

            EnsureSharedFunctionsInitialized(configuration, kmsProvider);

            return (service, userRepository, enterpriseRepository, refreshTokenRepository, passwordResetTokenRepository);
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
                IsActive = true,
                EnterpriseId = Guid.NewGuid()
            };
        }

        private static string ComputePasswordResetTokenHash(UserService service, string input)
        {
            var method = typeof(UserService)
                .GetMethod("ComputePasswordResetTokenHash", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.NotNull(method);
            var result = method.Invoke(service, new object[] { input });
            return Assert.IsType<string>(result);
        }

        private static void SetupUniqueUserLookup(Mock<IRepository<Shared.Entities.User>> userRepository, Shared.Entities.User? user)
        {
            var users = user == null
                ? Enumerable.Empty<Shared.Entities.User>()
                : new[] { user };

            userRepository.Setup(r => r.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<Shared.Entities.User, bool>>>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<Shared.Entities.User, object>>>()))
                .ReturnsAsync(users);
        }

        private static void EnsureSharedFunctionsInitialized(IConfiguration configuration, AWSKMSKeyProvider kmsProvider)
        {
            var serviceProvider = new ServiceCollection()
                .AddSingleton(configuration)
                .AddSingleton(kmsProvider)
                .BuildServiceProvider();

            SharedFunctions.Initialize(serviceProvider);
        }
    }
}
