using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.SupplierModule.Application.Services;
using Moq;
using Xunit;

namespace EvangelionERPV2.SupplierModule.Test
{
    public class SupplierServiceTests
    {
        private readonly Mock<IRepository<Supplier>> _supplierRepository;
        private readonly SupplierService _service;

        public SupplierServiceTests()
        {
            _supplierRepository = new Mock<IRepository<Supplier>>();
            _service = new SupplierService(_supplierRepository.Object);
        }

        [Fact]
        public async Task CreateAsync_IgnoresIncomingId_GeneratesServerId()
        {
            var incomingId = Guid.NewGuid();
            var supplier = new Supplier("Supplier", "11999999999", "supplier@test.com", "Street", "12345678901")
            {
                Id = incomingId
            };

            _supplierRepository
                .Setup(r => r.CreateAsync(It.IsAny<Supplier>()))
                .ReturnsAsync((Supplier entity) => entity);
            _supplierRepository
                .Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var result = await _service.CreateAsync(supplier);

            Assert.NotEqual(incomingId, result.Id);
            _supplierRepository.Verify(r => r.CreateAsync(It.Is<Supplier>(x => x.Id != incomingId)), Times.Once);
        }

        [Fact]
        public async Task CreateAsync_WhenValid_CreatesSupplier()
        {
            var supplier = new Supplier("Supplier", "11999999999", "supplier@test.com", "Street", "12345678901")
            {
                Id = Guid.NewGuid()
            };

            _supplierRepository.Setup(r => r.GetById(supplier.Id)).Returns((Supplier)null!);
            _supplierRepository.Setup(r => r.CreateAsync(It.IsAny<Supplier>())).ReturnsAsync(supplier);
            _supplierRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var result = await _service.CreateAsync(supplier);

            Assert.Same(supplier, result);
            _supplierRepository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void Update_WhenSupplierMissing_ThrowsNotFoundDatabaseException()
        {
            var supplier = new Supplier("Supplier", "11999999999", "supplier@test.com", "Street", "12345678901")
            {
                Id = Guid.NewGuid()
            };

            _supplierRepository.Setup(r => r.GetById(supplier.Id)).Returns((Supplier)null!);

            Assert.Throws<NotFoundDatabaseException>(() => _service.Update(supplier));
        }

        [Fact]
        public void Update_WhenValid_UpdatesSupplier()
        {
            var supplier = new Supplier("Supplier", "11999999999", "supplier@test.com", "Street", "12345678901")
            {
                Id = Guid.NewGuid()
            };

            _supplierRepository.Setup(r => r.GetById(supplier.Id)).Returns(supplier);
            _supplierRepository.Setup(r => r.Update(It.IsAny<Supplier>())).Returns(supplier);

            var result = _service.Update(supplier);
            var updatedAt = result.UpdatedAt ?? DateTime.MinValue;

            Assert.Same(supplier, result);
            Assert.NotEqual(DateTime.MinValue, updatedAt);
            _supplierRepository.Verify(r => r.Commit(), Times.Once);
        }

        [Fact]
        public void Delete_WhenSupplierMissing_ThrowsNotFoundDatabaseException()
        {
            var supplierId = Guid.NewGuid();

            _supplierRepository.Setup(r => r.GetById(supplierId)).Returns((Supplier)null!);

            Assert.Throws<NotFoundDatabaseException>(() => _service.Delete(supplierId));
        }

        [Fact]
        public void Delete_WhenValid_SetsInactive()
        {
            var supplier = new Supplier("Supplier", "11999999999", "supplier@test.com", "Street", "12345678901")
            {
                Id = Guid.NewGuid(),
                IsActive = true
            };

            _supplierRepository.Setup(r => r.GetById(supplier.Id)).Returns(supplier);
            _supplierRepository.Setup(r => r.Update(It.IsAny<Supplier>())).Returns(supplier);

            var result = _service.Delete(supplier.Id);
            var updatedAt = result.UpdatedAt ?? DateTime.MinValue;

            Assert.Same(supplier, result);
            Assert.False(result.IsActive ?? true);
            Assert.NotEqual(DateTime.MinValue, updatedAt);
            _supplierRepository.Verify(r => r.Commit(), Times.Once);
        }
    }
}
