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
    public class UserControllerLoginEnterpriseActivationTests
    {
        [Fact]
        public async Task LogInto_WhenEnterpriseIsInactive_ReturnsUnauthorized()
        {
            var enterpriseId = Guid.NewGuid();
            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = "user.test",
                Password = SharedFunctions.HashPassword("Password1!"),
                Email = "user@evangelion.com",
                FirstName = "User",
                LastName = "Test",
                BirthDate = DateTime.UtcNow.AddYears(-25),
                EnterpriseId = enterpriseId,
                IsActive = true
            };

            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            userRepository
                .Setup(x => x.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<User, object>>>() ))
                .ReturnsAsync([user]);

            var enterpriseRepository = new Mock<IRepository<Enterprise>>(MockBehavior.Strict);
            enterpriseRepository
                .Setup(x => x.GetByIdAsync(enterpriseId))
                .ReturnsAsync(new Enterprise
                {
                    Id = enterpriseId,
                    IsActive = false
                });

            var mapper = new Mock<IMapper>(MockBehavior.Strict).Object;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>(MockBehavior.Strict).Object, configuration);
            var tokenService = new TokenService(new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object, configuration);
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict).Object;

            var services = new ServiceCollection();
            services.AddSingleton(enterpriseRepository.Object);

            var controller = new UserController(
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

            var result = await controller.LogInto(new LoginRequestDTO
            {
                UserName = user.UserName,
                Password = "Password1!"
            });

            Assert.IsType<UnauthorizedResult>(result);
            userService.Verify(x => x.Update(It.IsAny<User>()), Times.Never);
        }
    }
}
