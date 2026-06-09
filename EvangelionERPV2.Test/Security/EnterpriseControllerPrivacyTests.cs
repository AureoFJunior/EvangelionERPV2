using AutoMapper;
using EvangelionERPV2.EnterpriseModule.Application.Interface;
using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    public class EnterpriseControllerPrivacyTests
    {
        [Fact]
        public async Task AddEnterprise_ReturnsDtoInsteadOfRawEntity()
        {
            var enterpriseId = Guid.NewGuid();
            var callerId = Guid.NewGuid();
            var enterprise = new Enterprise
            {
                Id = enterpriseId,
                Name = "NERV",
                Email = "hq@nerv.example",
                PhoneNumber = "+55 11 99999-0000",
                Adress = "Av. Paulista",
                Currency = "BRL",
                CurrentBalance = 1234.56,
                ShouldSendMonthlyBilling = true,
                IsActive = true
            };

            var service = new Mock<IEnterpriseService<Enterprise>>(MockBehavior.Strict);
            service
                .Setup(x => x.CreateAsync(It.IsAny<Enterprise>()))
                .ReturnsAsync(enterprise);

            var repository = new Mock<IRepository<Enterprise>>(MockBehavior.Strict);
            var enterpriseRepository = new Mock<IEnterpriseRepository<Enterprise>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            userRepository
                .Setup(x => x.GetByIdAsync(callerId))
                .ReturnsAsync(new User
                {
                    Id = callerId,
                    EnterpriseId = enterpriseId,
                    IsActive = true,
                    AccessLevel = 0
                });

            var mapper = new Mock<IMapper>(MockBehavior.Strict);
            mapper
                .Setup(x => x.Map<EnterpriseDTO>(It.IsAny<Enterprise>()))
                .Returns<Enterprise>(source => new EnterpriseDTO
                {
                    Id = source.Id,
                    Name = source.Name,
                    Email = source.Email,
                    PhoneNumber = source.PhoneNumber,
                    Adress = source.Adress,
                    Currency = source.Currency,
                    CurrentBalance = source.CurrentBalance,
                    IsActive = source.IsActive
                });

            var controller = new EnterpriseController(service.Object, repository.Object, enterpriseRepository.Object, userRepository.Object, mapper.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                            new Claim(ClaimTypes.Sid, callerId.ToString())
                        ], "TestAuth"))
                    }
                }
            };

            var result = await controller.AddEnterprise(new Enterprise
            {
                Name = "NERV",
                Email = "hq@nerv.example",
                PhoneNumber = "+55 11 99999-0000",
                Adress = "Av. Paulista",
                Currency = "BRL"
            });

            var ok = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<EnterpriseDTO>(ok.Value);
            Assert.Equal(enterpriseId, dto.Id);
            Assert.Equal("NERV", dto.Name);
            Assert.False(dto.GetType().GetProperty("ShouldSendMonthlyBilling") != null);
        }
    }
}
