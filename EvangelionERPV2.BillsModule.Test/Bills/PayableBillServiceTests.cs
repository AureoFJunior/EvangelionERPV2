using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.BillsModule.Test
{
    public class PayableBillServiceTests
    {
        [Fact]
        public async Task CreateAsync_ShouldSetAuditFieldsAndPersist()
        {
            var repo = new Mock<IRepository<PayableBill>>();
            repo.Setup(x => x.CreateAsync(It.IsAny<PayableBill>())).ReturnsAsync((PayableBill x) => x);
            var service = new PayableBillService(repo.Object);

            var entity = new PayableBill { Description = "Rent", Amount = 1000, EnterpriseId = Guid.NewGuid() };
            var result = await service.CreateAsync(entity);

            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.True(result.IsActive);
            repo.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateAsync_WhenNotFound_ShouldThrow()
        {
            var repo = new Mock<IRepository<PayableBill>>();
            repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((PayableBill?)null);
            var service = new PayableBillService(repo.Object);

            await Assert.ThrowsAsync<NotFoundDatabaseException>(() => service.UpdateAsync(new PayableBill { Id = Guid.NewGuid() }, Guid.NewGuid()));
        }

        [Fact]
        public async Task DeleteAsync_WhenFound_ShouldSoftDelete()
        {
            var enterpriseId = Guid.NewGuid();
            var entity = new PayableBill { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, IsActive = true };

            var repo = new Mock<IRepository<PayableBill>>();
            repo.Setup(x => x.GetByIdAsync(entity.Id)).ReturnsAsync(entity);
            var service = new PayableBillService(repo.Object);

            var result = await service.DeleteAsync(entity.Id, enterpriseId);

            Assert.False(result.IsActive);
            repo.Verify(x => x.Update(It.IsAny<PayableBill>()), Times.Once);
            repo.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
