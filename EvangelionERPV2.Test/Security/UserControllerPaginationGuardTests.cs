using Amazon.SecretsManager;
using AutoMapper;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    public class UserControllerPaginationGuardTests
    {
        [Fact]
        public async Task GetUsers_WhenPaginationMissing_UsesSafeDefaults()
        {
            var enterpriseId = Guid.NewGuid();
            var callerId = Guid.NewGuid();
            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            userRepository
                .Setup(r => r.GetByIdAsync(callerId))
                .ReturnsAsync(CreateAdminUser(callerId, enterpriseId));

            userRepository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Func<User, bool>>()))
                .Callback<int?, int?, Func<User, bool>?>((pageNumber, pageSize, _) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateEmployeeUser(enterpriseId)]);

            var controller = CreateController(userService.Object, userRepository.Object, mapper.Object, enterpriseId, callerId);

            var result = await controller.GetUsers();

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, capturedPageNumber);
            Assert.Equal(50, capturedPageSize);
        }

        [Fact]
        public async Task GetUsers_WhenPageSizeTooLarge_ClampsToMaximum()
        {
            var enterpriseId = Guid.NewGuid();
            var callerId = Guid.NewGuid();
            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            userRepository
                .Setup(r => r.GetByIdAsync(callerId))
                .ReturnsAsync(CreateAdminUser(callerId, enterpriseId));

            userRepository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Func<User, bool>>()))
                .Callback<int?, int?, Func<User, bool>?>((pageNumber, pageSize, _) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateEmployeeUser(enterpriseId)]);

            var controller = CreateController(userService.Object, userRepository.Object, mapper.Object, enterpriseId, callerId);

            var result = await controller.GetUsers(pageNumber: 4, pageSize: 1000);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(4, capturedPageNumber);
            Assert.Equal(200, capturedPageSize);
        }

        [Fact]
        public async Task GetUsers_WithPicturesAndLargePageSize_ClampsToPictureSafeMaximum()
        {
            var enterpriseId = Guid.NewGuid();
            var callerId = Guid.NewGuid();
            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            userRepository
                .Setup(r => r.GetByIdAsync(callerId))
                .ReturnsAsync(CreateAdminUser(callerId, enterpriseId));

            userRepository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Func<User, bool>>()))
                .Callback<int?, int?, Func<User, bool>?>((pageNumber, pageSize, _) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateEmployeeUser(enterpriseId)]);

            var controller = CreateController(userService.Object, userRepository.Object, mapper.Object, enterpriseId, callerId);

            var result = await controller.GetUsers(pageNumber: 2, pageSize: 1000, includePictures: true);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(2, capturedPageNumber);
            Assert.Equal(50, capturedPageSize);
        }

        private static Mock<IMapper> CreateMapper()
        {
            var mapper = new Mock<IMapper>(MockBehavior.Strict);
            mapper
                .Setup(m => m.Map<UserDTO>(It.IsAny<User>()))
                .Returns<User>(user => new UserDTO
                {
                    Id = user.Id,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Email = user.Email,
                    ProfilePicture = string.Empty,
                    AccessLevel = user.AccessLevel,
                    Language = user.Language,
                    ActualTheme = user.ActualTheme,
                    Enterprise = user.Enterprise
                });

            return mapper;
        }

        private static UserController CreateController(
            IUserService<User> userService,
            IRepository<User> userRepository,
            IMapper mapper,
            Guid enterpriseId,
            Guid callerId)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>(MockBehavior.Strict).Object, configuration);
            var refreshTokenRepository = new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object;
            var tokenService = new TokenService(refreshTokenRepository, configuration);
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict).Object;

            var controller = new UserController(
                userService,
                userRepository,
                mapper,
                configuration,
                kmsProvider,
                tokenService,
                emailService,
                new RecaptchaVerifier(new HttpClient(), configuration, kmsProvider));

            var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                new Claim(ClaimTypes.Sid, callerId.ToString())
            ], "UnitTestAuth"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = claims
                }
            };

            return controller;
        }

        private static User CreateAdminUser(Guid userId, Guid enterpriseId)
        {
            return new User
            {
                Id = userId,
                FirstName = "Admin",
                LastName = "User",
                UserName = "admin.user",
                Password = "hash",
                Email = "admin@example.com",
                BirthDate = DateTime.UtcNow.AddYears(-30),
                EnterpriseId = enterpriseId,
                IsActive = true,
                AccessLevel = (short)EnumAccessLevel.Admin
            };
        }

        private static User CreateEmployeeUser(Guid enterpriseId)
        {
            return new User
            {
                Id = Guid.NewGuid(),
                FirstName = "Employee",
                LastName = "User",
                UserName = "employee.user",
                Password = "hash",
                Email = "employee@example.com",
                BirthDate = DateTime.UtcNow.AddYears(-25),
                EnterpriseId = enterpriseId,
                IsActive = true,
                AccessLevel = (short)EnumAccessLevel.Employee
            };
        }
    }
}
