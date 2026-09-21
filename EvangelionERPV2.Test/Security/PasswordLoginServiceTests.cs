using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Web.Security;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The password check both logins share. Usernames are unique per enterprise, not globally, and
    /// neither login request carries a tenant discriminator — so the password is what picks the
    /// account, and the case where it cannot has to fail closed.
    /// </summary>
    public class PasswordLoginServiceTests
    {
        private const string SharedUserName = "caixa01";

        [Fact]
        public async Task Authenticate_ReturnsTheUser_WhenExactlyOneCandidateMatches()
        {
            var user = BuildUser("correct-horse");
            var service = new PasswordLoginService(BuildUserRepository(user).Object);

            var result = await service.AuthenticateAsync(SharedUserName, "correct-horse");

            Assert.NotNull(result.User);
            Assert.Equal(user.Id, result.User!.Id);
            Assert.Equal(PasswordLoginFailure.None, result.Failure);
        }

        [Fact]
        public async Task Authenticate_IsAmbiguous_WhenTwoTenantsShareUserNameAndPassword()
        {
            var service = new PasswordLoginService(
                BuildUserRepository(BuildUser("same-password"), BuildUser("same-password")).Object);

            var result = await service.AuthenticateAsync(SharedUserName, "same-password");

            Assert.Null(result.User);
            Assert.Equal(PasswordLoginFailure.Ambiguous, result.Failure);
        }

        [Fact]
        public async Task Authenticate_PicksTheTenant_WhenOnlyOnePasswordMatches()
        {
            var wanted = BuildUser("hers");
            var service = new PasswordLoginService(BuildUserRepository(BuildUser("his"), wanted).Object);

            var result = await service.AuthenticateAsync(SharedUserName, "hers");

            Assert.Equal(wanted.Id, result.User?.Id);
            Assert.Equal(PasswordLoginFailure.None, result.Failure);
        }

        [Fact]
        public async Task Authenticate_ReturnsNotFound_WhenNoPasswordMatches()
        {
            var service = new PasswordLoginService(BuildUserRepository(BuildUser("his")).Object);

            var result = await service.AuthenticateAsync(SharedUserName, "neither");

            Assert.Null(result.User);
            Assert.Equal(PasswordLoginFailure.NotFound, result.Failure);
        }

        [Fact]
        public async Task Authenticate_ReturnsNotFound_ForAnEmptyPassword_WithoutTouchingTheRepository()
        {
            var repository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var service = new PasswordLoginService(repository.Object);

            var result = await service.AuthenticateAsync(SharedUserName, "");

            Assert.Equal(PasswordLoginFailure.NotFound, result.Failure);
        }

        [Fact]
        public void NormalizeUserName_TrimsButDoesNotLowercase()
        {
            Assert.Equal("Caixa01", PasswordLoginService.NormalizeUserName("  Caixa01  "));
        }

        private static User BuildUser(string plainPassword)
        {
            return new User
            {
                Id = Guid.NewGuid(),
                UserName = SharedUserName,
                Password = SharedFunctions.HashPassword(plainPassword),
                Email = $"{Guid.NewGuid():N}@evangelion.com",
                FirstName = "User",
                LastName = "Test",
                BirthDate = DateTime.UtcNow.AddYears(-25),
                EnterpriseId = Guid.NewGuid(),
                IsActive = true
            };
        }

        private static Mock<IRepository<User>> BuildUserRepository(params User[] users)
        {
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            userRepository
                .Setup(x => x.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<User, object>>>()))
                .ReturnsAsync(users);
            return userRepository;
        }
    }
}
