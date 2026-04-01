using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.UserModule.Application.Token;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Cryptography;
using System.Text;
using System.Linq.Expressions;

namespace EvangelionERPV2.UserModule.Test.User
{
    public class TokenServiceRefreshTokenTests
    {
        [Fact]
        public async Task SaveRefreshTokenAsync_WhenValid_RunsInsideSingleTransaction()
        {
            var (service, refreshTokenRepository) = CreateService();
            var userId = Guid.NewGuid();
            var activeToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = "old-hash",
                ExpiresAt = DateTime.UtcNow.AddDays(1),
                IsActive = true
            };

            refreshTokenRepository
                .Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<Task>, CancellationToken>(async (operation, _) => await operation());

            refreshTokenRepository
                .Setup(r => r.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<Expression<Func<RefreshToken, bool>>>(),
                    It.IsAny<Expression<Func<RefreshToken, object>>>()))
                .ReturnsAsync([activeToken]);

            refreshTokenRepository
                .Setup(r => r.Update(It.IsAny<RefreshToken>()))
                .Returns<RefreshToken>(entity => entity);

            refreshTokenRepository
                .Setup(r => r.CreateAsync(It.IsAny<RefreshToken>()))
                .ReturnsAsync((RefreshToken entity) => entity);

            refreshTokenRepository
                .Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            await service.SaveRefreshTokenAsync(userId, "new-refresh-token");

            Assert.NotNull(activeToken.RevokedAt);
            refreshTokenRepository.Verify(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()), Times.Once);
            refreshTokenRepository.Verify(r => r.Update(It.IsAny<RefreshToken>()), Times.AtLeastOnce);
            refreshTokenRepository.Verify(r => r.CreateAsync(It.IsAny<RefreshToken>()), Times.Once);
            refreshTokenRepository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveRefreshTokenAsync_WhenUserIdIsEmpty_ThrowsArgumentException()
        {
            var (service, refreshTokenRepository) = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveRefreshTokenAsync(Guid.Empty, "valid-token"));

            refreshTokenRepository.Verify(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ValidateRefreshTokenAsync_UsesSingleRecordPageSize()
        {
            var (service, refreshTokenRepository) = CreateService();
            var userId = Guid.NewGuid();
            var refreshToken = "valid-refresh-token";
            var tokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
            int? capturedPageSize = null;

            refreshTokenRepository
                .Setup(r => r.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<Expression<Func<RefreshToken, bool>>>(),
                    It.IsAny<Expression<Func<RefreshToken, object>>>()))
                .Callback<bool, int?, int?, Expression<Func<RefreshToken, bool>>, Expression<Func<RefreshToken, object>>>(
                    (_, _, pageSize, _, _) => capturedPageSize = pageSize)
                .ReturnsAsync([new RefreshToken
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    TokenHash = tokenHash,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                    IsActive = true,
                    RevokedAt = null
                }]);

            var result = await service.ValidateRefreshTokenAsync(userId, refreshToken);

            Assert.True(result);
            Assert.Equal(1, capturedPageSize);
        }

        [Fact]
        public async Task RevokeRefreshTokenAsync_UsesSingleRecordPageSize_AndRevokesToken()
        {
            var (service, refreshTokenRepository) = CreateService();
            var userId = Guid.NewGuid();
            var refreshToken = "revoke-refresh-token";
            var tokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
            int? capturedPageSize = null;

            var tokenEntity = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = tokenHash,
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                IsActive = true,
                RevokedAt = null
            };

            refreshTokenRepository
                .Setup(r => r.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<Expression<Func<RefreshToken, bool>>>(),
                    It.IsAny<Expression<Func<RefreshToken, object>>>()))
                .Callback<bool, int?, int?, Expression<Func<RefreshToken, bool>>, Expression<Func<RefreshToken, object>>>(
                    (_, _, pageSize, _, _) => capturedPageSize = pageSize)
                .ReturnsAsync([tokenEntity]);

            refreshTokenRepository
                .Setup(r => r.Update(It.IsAny<RefreshToken>()))
                .Returns<RefreshToken>(entity => entity);

            refreshTokenRepository
                .Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            await service.RevokeRefreshTokenAsync(userId, refreshToken);

            Assert.Equal(1, capturedPageSize);
            Assert.NotNull(tokenEntity.RevokedAt);
            refreshTokenRepository.Verify(r => r.Update(It.IsAny<RefreshToken>()), Times.Once);
            refreshTokenRepository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        private static (TokenService service, Mock<IRepository<RefreshToken>> refreshTokenRepository) CreateService()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JwtSettings:Issuer"] = "issuer",
                    ["JwtSettings:Audience"] = "audience",
                    ["JwtSettings:AccessTokenMinutes"] = "60",
                    ["JwtSettings:RefreshTokenDays"] = "7"
                })
                .Build();

            var refreshTokenRepository = new Mock<IRepository<RefreshToken>>();
            var service = new TokenService(refreshTokenRepository.Object, configuration);

            return (service, refreshTokenRepository);
        }
    }
}
