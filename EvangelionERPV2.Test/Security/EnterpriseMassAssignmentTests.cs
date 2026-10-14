using AutoMapper;
using EvangelionERPV2.EnterpriseModule.Application.Interface;
using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// <c>CurrentBalance</c> is money the system computes and <c>CashFlowForecastService</c> reads.
    /// Until the write endpoints bound the entity itself, anyone holding <c>enterprise.update</c>
    /// could set it from a request body — a silent rewrite of the number the whole forecast rests on.
    /// </summary>
    public class EnterpriseMassAssignmentTests
    {
        private const decimal StoredBalance = 4321.99m;

        [Fact]
        public async Task UpdateEnterprise_CannotMoveCurrentBalance()
        {
            var context = new EnterpriseControllerContext();

            var result = await context.Controller.UpdateEnterprise(new UpdateEnterpriseRequestDTO
            {
                Id = context.EnterpriseId,
                Name = "NERV",
                Email = "hq@nerv.example",
                PhoneNumber = "+55 11 99999-0000",
                Adress = "Av. Paulista",
                Currency = "BRL"
            });

            Assert.IsType<OkObjectResult>(result);

            Assert.NotNull(context.Updated);
            Assert.Equal(StoredBalance, context.Updated!.CurrentBalance);
            Assert.Equal("NERV", context.Updated.Name);
        }

        [Fact]
        public void UpdateEnterpriseRequest_HasNoBalanceOrAuditFields()
        {
            foreach (var requestType in new[] { typeof(UpdateEnterpriseRequestDTO), typeof(CreateEnterpriseRequestDTO) })
            {
                foreach (var forbidden in new[] { "CurrentBalance", "CreatedAt", "UpdatedAt", "IsActive" })
                    Assert.Null(requestType.GetProperty(forbidden));
            }

            Assert.NotNull(typeof(UpdateEnterpriseRequestDTO).GetProperty("Id"));
            Assert.Null(typeof(CreateEnterpriseRequestDTO).GetProperty("Id"));
        }

        [Fact]
        public async Task UpdateEnterprise_ForAnotherTenant_IsForbidden()
        {
            var context = new EnterpriseControllerContext();

            var result = await context.Controller.UpdateEnterprise(new UpdateEnterpriseRequestDTO
            {
                Id = Guid.NewGuid(),
                Name = "SEELE",
                Email = "hq@seele.example",
                PhoneNumber = "+55 11 98888-0000",
                Adress = "Av. Paulista"
            });

            Assert.IsType<ForbidResult>(result);
            Assert.Null(context.Updated);
        }

        private sealed class EnterpriseControllerContext
        {
            public Guid EnterpriseId { get; } = Guid.NewGuid();
            public Guid CallerId { get; } = Guid.NewGuid();

            /// <summary>The entity as the service received it.</summary>
            public Enterprise? Updated { get; private set; }

            public EnterpriseController Controller { get; }

            public EnterpriseControllerContext()
            {
                var stored = new Enterprise
                {
                    Id = EnterpriseId,
                    Name = "NERV",
                    Email = "hq@nerv.example",
                    PhoneNumber = "+55 11 99999-0000",
                    Adress = "Av. Paulista",
                    Currency = "BRL",
                    CurrentBalance = StoredBalance,
                    IsActive = true
                };

                var service = new Mock<IEnterpriseService<Enterprise>>(MockBehavior.Strict);
                service
                    .Setup(x => x.Update(It.IsAny<Enterprise>()))
                    .Callback<Enterprise>(enterprise => Updated = enterprise)
                    .Returns<Enterprise>(enterprise => enterprise);

                var repository = new Mock<IRepository<Enterprise>>(MockBehavior.Strict);
                repository
                    .Setup(x => x.GetByIdAsync(EnterpriseId))
                    .ReturnsAsync(stored);

                var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
                userRepository
                    .Setup(x => x.GetByIdAsync(CallerId))
                    .ReturnsAsync(new User
                    {
                        Id = CallerId,
                        EnterpriseId = EnterpriseId,
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
                        CurrentBalance = source.CurrentBalance
                    });

                Controller = new EnterpriseController(
                    service.Object,
                    repository.Object,
                    new Mock<IEnterpriseRepository<Enterprise>>(MockBehavior.Strict).Object,
                    mapper.Object,
                    new Mock<IAuthorizationService>().Object)
                {
                    ControllerContext = new ControllerContext
                    {
                        HttpContext = new DefaultHttpContext
                        {
                            User = new ClaimsPrincipal(new ClaimsIdentity(
                            [
                                new Claim(ClaimTypes.GroupSid, EnterpriseId.ToString()),
                                new Claim(ClaimTypes.Sid, CallerId.ToString())
                            ], "TestAuth"))
                        }
                    }
                };
            }
        }
    }
}
