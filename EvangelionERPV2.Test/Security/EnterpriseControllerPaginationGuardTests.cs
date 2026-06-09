using AutoMapper;
using EvangelionERPV2.EnterpriseModule.Application.Interface;
using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    public class EnterpriseControllerPaginationGuardTests
    {
        [Fact]
        public async Task GetEnterprises_WhenPaginationMissing_UsesSafeDefaults()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var enterpriseService = new Mock<IEnterpriseService<Enterprise>>(MockBehavior.Strict);
            var enterpriseRepository = new Mock<IRepository<Enterprise>>(MockBehavior.Strict);
            var customEnterpriseRepository = new Mock<IEnterpriseRepository<Enterprise>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            userRepository
                .Setup(r => r.GetByIdAsync(userId))
                .ReturnsAsync(CreateUser(userId, enterpriseId, EnumAccessLevel.Admin));

            enterpriseRepository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Func<Enterprise, bool>>()))
                .Callback<int?, int?, Func<Enterprise, bool>?>((pageNumber, pageSize, _) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateEnterprise()]);

            var controller = new EnterpriseController(
                enterpriseService.Object,
                enterpriseRepository.Object,
                customEnterpriseRepository.Object,
                userRepository.Object,
                mapper.Object);
            ConfigureController(controller, enterpriseId, userId);

            var result = await controller.GetEnterprises();

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, capturedPageNumber);
            Assert.Equal(50, capturedPageSize);
        }

        [Fact]
        public async Task GetEnterprisesByFilter_WhenPageSizeTooLarge_ClampsToMaximum()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var enterpriseService = new Mock<IEnterpriseService<Enterprise>>(MockBehavior.Strict);
            var enterpriseRepository = new Mock<IRepository<Enterprise>>(MockBehavior.Strict);
            var customEnterpriseRepository = new Mock<IEnterpriseRepository<Enterprise>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            userRepository
                .Setup(r => r.GetByIdAsync(userId))
                .ReturnsAsync(CreateUser(userId, enterpriseId, EnumAccessLevel.Admin));

            customEnterpriseRepository
                .Setup(r => r.GetAllAsyncFiltering(It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Enterprise>()))
                .Callback<bool, int?, int?, Enterprise>((_, pageNumber, pageSize, _) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateEnterprise()]);

            var controller = new EnterpriseController(
                enterpriseService.Object,
                enterpriseRepository.Object,
                customEnterpriseRepository.Object,
                userRepository.Object,
                mapper.Object);
            ConfigureController(controller, enterpriseId, userId);

            var result = await controller.GetEnterprisesByFilter(
                enterprise: new Enterprise(),
                descending: true,
                pageNumber: 2,
                pageSize: 5000);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(2, capturedPageNumber);
            Assert.Equal(200, capturedPageSize);
        }

        private static Mock<IMapper> CreateMapper()
        {
            var mapper = new Mock<IMapper>(MockBehavior.Strict);
            mapper.Setup(m => m.Map<IEnumerable<EnterpriseDTO>>(It.IsAny<IEnumerable<Enterprise>>()))
                .Returns<IEnumerable<Enterprise>>(enterprises =>
                    enterprises.Select(enterprise => new EnterpriseDTO
                    {
                        Id = enterprise.Id,
                        Name = enterprise.Name
                    }));

            return mapper;
        }

        private static Enterprise CreateEnterprise()
        {
            return new Enterprise
            {
                Id = Guid.NewGuid(),
                Name = "Enterprise Name",
                IsActive = true
            };
        }

        private static User CreateUser(Guid userId, Guid enterpriseId, EnumAccessLevel accessLevel)
        {
            return new User
            {
                Id = userId,
                EnterpriseId = enterpriseId,
                IsActive = true,
                AccessLevel = (short)accessLevel
            };
        }

        private static void ConfigureController(EnterpriseController controller, Guid enterpriseId, Guid userId)
        {
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                new Claim(ClaimTypes.Sid, userId.ToString())
            ], "TestAuth");

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            };
        }
    }
}
