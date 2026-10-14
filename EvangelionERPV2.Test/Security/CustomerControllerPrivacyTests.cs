using AutoMapper;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Text.Json;

namespace EvangelionERPV2.Test.Security
{
    public class CustomerControllerPrivacyTests
    {
        [Fact]
        public async Task AddCustomer_WithInvalidModelState_ReturnsGenericBadRequestMessage()
        {
            var controller = CreateController();
            controller.ModelState.AddModelError("Email", "Customer email is invalid.");

            var result = await controller.AddCustomer(new CreateCustomerRequestDTO());

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid request payload.", badRequest.Value);
        }

        [Fact]
        public void CustomerDto_Document_IsNotSerializedInApiResponses()
        {
            var dto = new CustomerDTO
            {
                Id = Guid.NewGuid(),
                Name = "Customer",
                PhoneNumber = "11999999999",
                Email = "customer@example.com",
                Adress = "Av. Example, 123",
                Document = "12345678901",
                EnterpriseId = Guid.NewGuid()
            };

            var json = JsonSerializer.Serialize(dto);

            Assert.DoesNotContain("\"Document\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"Name\":\"Customer\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"Email\":\"customer@example.com\"", json, StringComparison.OrdinalIgnoreCase);
        }

        private static CustomerController CreateController()
        {
            var customerService = new Mock<ICustomerService<Customer>>(MockBehavior.Strict);
            var customerRepository = new Mock<IRepository<Customer>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var mapper = new Mock<IMapper>(MockBehavior.Strict);

            return new CustomerController(
                customerService.Object,
                customerRepository.Object,
                userRepository.Object,
                mapper.Object);
        }
    }
}
