using EvangelionERPV2.CustomerModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Moq;
using Xunit;

namespace EvangelionERPV2.CustomerModule.Test
{
    public class CustomerServiceTests
    {
        private readonly Mock<IRepository<Customer>> _customerRepository;
        private readonly CustomerService _service;

        public CustomerServiceTests()
        {
            _customerRepository = new Mock<IRepository<Customer>>();
            _service = new CustomerService(_customerRepository.Object);
        }

        [Fact]
        public async Task CreateAsync_IgnoresIncomingId_GeneratesServerId()
        {
            var incomingId = Guid.NewGuid();
            var customer = new Customer("Customer", "11999999999", "customer@test.com", "Street", "12345678901")
            {
                Id = incomingId
            };

            _customerRepository
                .Setup(r => r.CreateAsync(It.IsAny<Customer>()))
                .ReturnsAsync((Customer entity) => entity);
            _customerRepository
                .Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var result = await _service.CreateAsync(customer);

            Assert.NotEqual(incomingId, result.Id);
            _customerRepository.Verify(r => r.CreateAsync(It.Is<Customer>(x => x.Id != incomingId)), Times.Once);
        }

        [Fact]
        public async Task CreateAsync_WhenValid_CreatesCustomer()
        {
            var customer = new Customer("Customer", "11999999999", "customer@test.com", "Street", "12345678901")
            {
                Id = Guid.NewGuid()
            };

            _customerRepository.Setup(r => r.GetById(customer.Id)).Returns((Customer)null!);
            _customerRepository.Setup(r => r.CreateAsync(It.IsAny<Customer>())).ReturnsAsync(customer);
            _customerRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var result = await _service.CreateAsync(customer);

            Assert.Same(customer, result);
            _customerRepository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void Update_WhenCustomerMissing_ThrowsNotFoundDatabaseException()
        {
            var customer = new Customer("Customer", "11999999999", "customer@test.com", "Street", "12345678901")
            {
                Id = Guid.NewGuid()
            };

            _customerRepository.Setup(r => r.GetById(customer.Id)).Returns((Customer)null!);

            Assert.Throws<NotFoundDatabaseException>(() => _service.Update(customer));
        }

        [Fact]
        public void Update_WhenValid_UpdatesCustomer()
        {
            var customer = new Customer("Customer", "11999999999", "customer@test.com", "Street", "12345678901")
            {
                Id = Guid.NewGuid()
            };

            _customerRepository.Setup(r => r.GetById(customer.Id)).Returns(customer);
            _customerRepository.Setup(r => r.Update(It.IsAny<Customer>())).Returns(customer);

            var result = _service.Update(customer);
            var updatedAt = result.UpdatedAt ?? DateTime.MinValue;

            Assert.Same(customer, result);
            Assert.NotEqual(DateTime.MinValue, updatedAt);
            _customerRepository.Verify(r => r.Commit(), Times.Once);
        }

        [Fact]
        public void Delete_WhenCustomerMissing_ThrowsNotFoundDatabaseException()
        {
            var customerId = Guid.NewGuid();

            _customerRepository.Setup(r => r.GetById(customerId)).Returns((Customer)null!);

            Assert.Throws<NotFoundDatabaseException>(() => _service.Delete(customerId));
        }

        [Fact]
        public void Delete_WhenValid_SetsInactive()
        {
            var customer = new Customer("Customer", "11999999999", "customer@test.com", "Street", "12345678901")
            {
                Id = Guid.NewGuid(),
                IsActive = true
            };

            _customerRepository.Setup(r => r.GetById(customer.Id)).Returns(customer);
            _customerRepository.Setup(r => r.Update(It.IsAny<Customer>())).Returns(customer);

            var result = _service.Delete(customer.Id);
            var updatedAt = result.UpdatedAt ?? DateTime.MinValue;

            Assert.Same(customer, result);
            Assert.False(result.IsActive ?? true);
            Assert.NotEqual(DateTime.MinValue, updatedAt);
            _customerRepository.Verify(r => r.Commit(), Times.Once);
        }
    }
}
