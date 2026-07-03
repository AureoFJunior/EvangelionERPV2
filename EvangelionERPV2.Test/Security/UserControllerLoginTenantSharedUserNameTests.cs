using Amazon.SecretsManager;
using AutoMapper;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    public class UserControllerLoginTenantSharedUserNameTests
    {
        private const string SharedUserName = "admin";

        [Fact]
        public async Task LogInto_WhenTwoTenantsShareUserName_ResolvesUserByPassword()
        {
            var tenantAUser = BuildUser("PasswordA1!");
            var tenantBUser = BuildUser("PasswordB1!");

            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = BuildUserRepository(tenantAUser, tenantBUser);

            // Strict mock: only tenant B's enterprise lookup is set up. If the controller
            // resolved tenant A's user (or none), this lookup would never happen and the
            // returned-inactive-enterprise Unauthorized below could not be reached this way.
            var enterpriseRepository = new Mock<IRepository<Enterprise>>(MockBehavior.Strict);
            enterpriseRepository
                .Setup(x => x.GetByIdAsync(tenantBUser.EnterpriseId!.Value))
                .ReturnsAsync(new Enterprise
                {
                    Id = tenantBUser.EnterpriseId!.Value,
                    IsActive = false
                });

            var controller = BuildController(userService, userRepository, enterpriseRepository);

            var result = await controller.LogInto(new LoginRequestDTO
            {
                UserName = SharedUserName,
                Password = "PasswordB1!"
            });

            Assert.IsType<UnauthorizedResult>(result);
            enterpriseRepository.Verify(x => x.GetByIdAsync(tenantBUser.EnterpriseId!.Value), Times.Once);
        }

        [Fact]
        public async Task LogInto_WhenTwoTenantsShareUserNameAndPassword_ReturnsUnauthorizedWithoutResolvingTenant()
        {
            var tenantAUser = BuildUser("SamePassword1!");
            var tenantBUser = BuildUser("SamePassword1!");

            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = BuildUserRepository(tenantAUser, tenantBUser);
            var enterpriseRepository = new Mock<IRepository<Enterprise>>(MockBehavior.Strict);

            var controller = BuildController(userService, userRepository, enterpriseRepository);

            var result = await controller.LogInto(new LoginRequestDTO
            {
                UserName = SharedUserName,
                Password = "SamePassword1!"
            });

            Assert.IsType<UnauthorizedResult>(result);
            enterpriseRepository.Verify(
                x => x.GetByIdAsync(It.IsAny<Guid>()),
                Times.Never);
            userService.Verify(x => x.Update(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task LogInto_WhenTwoTenantsShareUserNameAndPasswordMatchesNeither_ReturnsUnauthorized()
        {
            var tenantAUser = BuildUser("PasswordA1!");
            var tenantBUser = BuildUser("PasswordB1!");

            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = BuildUserRepository(tenantAUser, tenantBUser);
            var enterpriseRepository = new Mock<IRepository<Enterprise>>(MockBehavior.Strict);

            var controller = BuildController(userService, userRepository, enterpriseRepository);

            var result = await controller.LogInto(new LoginRequestDTO
            {
                UserName = SharedUserName,
                Password = "WrongPassword1!"
            });

            Assert.IsType<UnauthorizedResult>(result);
            userService.Verify(x => x.Update(It.IsAny<User>()), Times.Never);
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

        private static UserController BuildController(
            Mock<IUserService<User>> userService,
            Mock<IRepository<User>> userRepository,
            Mock<IRepository<Enterprise>> enterpriseRepository)
        {
            var mapper = new Mock<IMapper>(MockBehavior.Strict).Object;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>(MockBehavior.Strict).Object, configuration);
            var tokenService = new TokenService(new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object, configuration);
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict).Object;

            var services = new ServiceCollection();
            services.AddSingleton(enterpriseRepository.Object);

            return new UserController(
                userService.Object,
                userRepository.Object,
                mapper,
                configuration,
                kmsProvider,
                tokenService,
                emailService,
                new RecaptchaVerifier(new HttpClient(), configuration, kmsProvider))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        RequestServices = services.BuildServiceProvider()
                    }
                }
            };
        }
    }
}
