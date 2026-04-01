using AutoMapper;
using EvangelionERPV2.CustomerModule.Application.Interface;
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
    public class CustomerControllerPaginationGuardTests
    {
        [Fact]
        public async Task GetCustomers_WhenPaginationMissing_UsesSafeDefaults()
        {
            var enterpriseId = Guid.NewGuid();
            var customerService = new Mock<ICustomerService<Customer>>(MockBehavior.Strict);
            var customerRepository = new Mock<IRepository<Customer>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            customerRepository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Func<Customer, bool>>()))
                .Callback<int?, int?, Func<Customer, bool>?>((pageNumber, pageSize, _) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateCustomer(enterpriseId)]);

            var controller = CreateController(
                customerService.Object,
                customerRepository.Object,
                userRepository.Object,
                mapper.Object,
                enterpriseId);

            var result = await controller.GetCustomers();

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, capturedPageNumber);
            Assert.Equal(50, capturedPageSize);
        }

        [Fact]
        public async Task GetCustomersByFilter_WhenPageSizeTooLarge_ClampsToMaximum()
        {
            var enterpriseId = Guid.NewGuid();
            var customerService = new Mock<ICustomerService<Customer>>(MockBehavior.Strict);
            var customerRepository = new Mock<IRepository<Customer>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            customerRepository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Func<Customer, bool>>()))
                .Callback<int?, int?, Func<Customer, bool>?>((pageNumber, pageSize, _) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateCustomer(enterpriseId)]);

            var controller = CreateController(
                customerService.Object,
                customerRepository.Object,
                userRepository.Object,
                mapper.Object,
                enterpriseId);

            var result = await controller.GetCustomersByFilter(
                filter: new CustomerFilterRequestDTO(),
                descending: true,
                pageNumber: 3,
                pageSize: 5000);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(3, capturedPageNumber);
            Assert.Equal(200, capturedPageSize);
        }

        private static Mock<IMapper> CreateMapper()
        {
            var mapper = new Mock<IMapper>(MockBehavior.Strict);
            mapper.Setup(m => m.Map<IEnumerable<CustomerDTO>>(It.IsAny<IEnumerable<Customer>>()))
                .Returns<IEnumerable<Customer>>(customers =>
                    customers.Select(customer => new CustomerDTO
                    {
                        Id = customer.Id,
                        Name = customer.Name,
                        Email = customer.Email
                    }));

            return mapper;
        }

        private static CustomerController CreateController(
            ICustomerService<Customer> customerService,
            IRepository<Customer> customerRepository,
            IRepository<User> userRepository,
            IMapper mapper,
            Guid enterpriseId)
        {
            var controller = new CustomerController(customerService, customerRepository, userRepository, mapper);

            var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())
            ], "TestAuth"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = claims
                }
            };

            return controller;
        }

        private static Customer CreateCustomer(Guid enterpriseId)
        {
            return new Customer
            {
                Id = Guid.NewGuid(),
                Name = "Customer",
                Email = "customer@example.com",
                PhoneNumber = "0000000000",
                Adress = "Address",
                EnterpriseId = enterpriseId,
                IsActive = true
            };
        }
    }
}
