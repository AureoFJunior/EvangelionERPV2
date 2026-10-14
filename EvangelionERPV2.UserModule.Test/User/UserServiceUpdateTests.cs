using Amazon.SecretsManager;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Linq.Expressions;

namespace EvangelionERPV2.UserModule.Test.User
{
    public class UserServiceUpdateTests
    {
        [Fact]
        public void Update_WhenSensitiveFieldsAreOmitted_PreservesPasswordAndProfilePicture()
        {
            var userRepository = new Mock<IRepository<Shared.Entities.User>>();
            var existingUser = new Shared.Entities.User
            {
                Id = Guid.NewGuid(),
                FirstName = "Rei",
                LastName = "Ayanami",
                UserName = "rei",
                Email = "rei@example.com",
                Password = "HASHED-PASSWORD",
                ProfilePicture = "encrypted-picture-key",
                EnterpriseId = Guid.NewGuid(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-10)
            };
            Shared.Entities.User? updatedUser = null;

            userRepository
                .Setup(repository => repository.GetById(existingUser.Id))
                .Returns(existingUser);
            userRepository
                .Setup(repository => repository.GetByCondition(It.IsAny<Func<Shared.Entities.User, bool>>()))
                .Returns([]);
            userRepository
                .Setup(repository => repository.Update(It.IsAny<Shared.Entities.User>()))
                .Callback<Shared.Entities.User>(user => updatedUser = user)
                .Returns((Shared.Entities.User user) => user);
            userRepository
                .Setup(repository => repository.Commit(It.IsAny<CancellationToken>()));

            var service = CreateService(userRepository.Object);
            var payload = new Shared.Entities.User
            {
                Id = existingUser.Id,
                FirstName = "Rei II",
                LastName = existingUser.LastName,
                UserName = existingUser.UserName,
                Email = existingUser.Email,
                AccessLevel = existingUser.AccessLevel,
                EnterpriseId = existingUser.EnterpriseId,
                Password = string.Empty,
                ProfilePicture = string.Empty
            };

            service.Update(payload);

            Assert.NotNull(updatedUser);
            Assert.Equal(existingUser.Password, updatedUser!.Password);
            Assert.Equal(existingUser.ProfilePicture, updatedUser.ProfilePicture);
            Assert.Equal(existingUser.CreatedAt, updatedUser.CreatedAt);
            Assert.Equal(existingUser.IsActive, updatedUser.IsActive);
            Assert.Equal("Rei II", updatedUser.FirstName);
        }

        [Fact]
        public async Task CreateAsync_WhenActiveEmailExistsInAnotherEnterprise_ThrowsArgumentException()
        {
            var userRepository = new Mock<IRepository<Shared.Entities.User>>();
            var existingUser = new Shared.Entities.User
            {
                Id = Guid.NewGuid(),
                Email = "rei@example.com",
                EnterpriseId = Guid.NewGuid(),
                IsActive = true
            };

            SetupGlobalActiveEmailLookup(userRepository, [existingUser]);

            var service = CreateService(userRepository.Object);
            var duplicateUser = new Shared.Entities.User
            {
                Id = Guid.NewGuid(),
                FirstName = "Rei",
                LastName = "Duplicate",
                UserName = "rei.duplicate",
                Email = " REI@example.com ",
                Password = "Password1",
                EnterpriseId = Guid.NewGuid(),
                IsActive = true
            };

            var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(duplicateUser));

            Assert.Equal("Email is already in use.", exception.Message);
            userRepository.Verify(repository => repository.CreateAsync(It.IsAny<Shared.Entities.User>()), Times.Never);
        }

        [Fact]
        public void Update_WhenActiveEmailExistsInAnotherEnterprise_ThrowsArgumentException()
        {
            var userRepository = new Mock<IRepository<Shared.Entities.User>>();
            var enterpriseId = Guid.NewGuid();
            var existingUser = new Shared.Entities.User
            {
                Id = Guid.NewGuid(),
                FirstName = "Rei",
                LastName = "Ayanami",
                UserName = "rei",
                Email = "rei@example.com",
                Password = "HASHED-PASSWORD",
                ProfilePicture = "encrypted-picture-key",
                EnterpriseId = enterpriseId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-10)
            };
            var duplicateUser = new Shared.Entities.User
            {
                Id = Guid.NewGuid(),
                Email = "asuka@example.com",
                EnterpriseId = Guid.NewGuid(),
                IsActive = true
            };

            userRepository
                .Setup(repository => repository.GetById(existingUser.Id))
                .Returns(existingUser);
            userRepository
                .Setup(repository => repository.GetByCondition(It.IsAny<Func<Shared.Entities.User, bool>>()))
                .Returns((Func<Shared.Entities.User, bool> predicate) => new[] { duplicateUser }.Where(predicate).ToList());

            var service = CreateService(userRepository.Object);
            var payload = new Shared.Entities.User
            {
                Id = existingUser.Id,
                FirstName = existingUser.FirstName,
                LastName = existingUser.LastName,
                UserName = existingUser.UserName,
                Email = " ASUKA@example.com ",
                AccessLevel = existingUser.AccessLevel,
                EnterpriseId = existingUser.EnterpriseId,
                Password = string.Empty,
                ProfilePicture = string.Empty
            };

            var exception = Assert.Throws<ArgumentException>(() => service.Update(payload));

            Assert.Equal("Email is already in use.", exception.Message);
            userRepository.Verify(repository => repository.Update(It.IsAny<Shared.Entities.User>()), Times.Never);
        }

        private static UserService CreateService(IRepository<Shared.Entities.User> userRepository)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var kmsProvider = new AWSKMSKeyProvider(
                new Mock<IAmazonSecretsManager>(MockBehavior.Strict).Object,
                configuration);

            return new UserService(
                userRepository,
                new Mock<IRepository<Enterprise>>(MockBehavior.Strict).Object,
                new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object,
                new Mock<IRepository<PasswordResetToken>>(MockBehavior.Strict).Object,
                configuration,
                kmsProvider);
        }

        private static void SetupGlobalActiveEmailLookup(
            Mock<IRepository<Shared.Entities.User>> userRepository,
            IEnumerable<Shared.Entities.User> users)
        {
            userRepository
                .Setup(repository => repository.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<Expression<Func<Shared.Entities.User, bool>>>(),
                    It.IsAny<Expression<Func<Shared.Entities.User, object>>>()))
                .ReturnsAsync((
                    bool descending,
                    int? pageNumber,
                    int? pageSize,
                    Expression<Func<Shared.Entities.User, bool>>? predicate,
                    Expression<Func<Shared.Entities.User, object>>? orderBy) =>
                {
                    var query = users;
                    if (predicate != null)
                        query = query.Where(predicate.Compile());

                    return query.ToList();
                });
        }
    }
}
