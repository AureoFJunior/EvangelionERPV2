using EvangelionERPV2.AuditModule.Application.Services;
using EvangelionERPV2.AuditModule.Domain.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;

using Moq;

namespace EvangelionERPV2.AuditModule.Test
{
    /// <summary>
    /// The audit trail is the record somebody reaches for after something went wrong, so these
    /// tests are about the two ways it can quietly stop being that: an unbounded page that times
    /// the query out, and a retention sweep that deletes the wrong window.
    /// </summary>
    public class AuditTrailServiceTests
    {
        private static readonly Guid EnterpriseId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        #region Paging

        [Theory]
        [InlineData(null, 1)]
        [InlineData(0, 1)]
        [InlineData(-5, 1)]
        [InlineData(3, 3)]
        public async Task GetAllAsyncFiltering_NormalizesThePageNumber(int? requested, int expected)
        {
            var context = new ServiceContext();

            await context.Service.GetAllAsyncFiltering(EnterpriseId, descending: true, requested, pageSize: 10);

            Assert.Equal(expected, context.LastPageNumber);
        }

        [Theory]
        [InlineData(null, 50)]
        [InlineData(0, 50)]
        [InlineData(-1, 50)]
        [InlineData(25, 25)]
        [InlineData(500, 200)]
        public async Task GetAllAsyncFiltering_ClampsThePageSize(int? requested, int expected)
        {
            var context = new ServiceContext();

            await context.Service.GetAllAsyncFiltering(EnterpriseId, descending: true, pageNumber: 1, requested);

            Assert.Equal(expected, context.LastPageSize);
        }

        [Fact]
        public async Task GetAllAsyncFiltering_PassesTheTenantAndTheFilterStraightThrough()
        {
            var context = new ServiceContext();
            var filter = new AuditTrailFilterDTO { EntityName = "Customer" };

            await context.Service.GetAllAsyncFiltering(EnterpriseId, descending: false, 1, 10, filter);

            Assert.Equal(EnterpriseId, context.LastEnterpriseId);
            Assert.False(context.LastDescending);
            Assert.Same(filter, context.LastFilter);
        }

        #endregion

        #region Retention sweep

        [Fact]
        public async Task DeleteOlderThanAsync_WithoutATenant_DeletesNothingAndAsksNothing()
        {
            var context = new ServiceContext();

            var deleted = await context.Service.DeleteOlderThanAsync(Guid.Empty, DateTime.UtcNow);

            Assert.Equal(0, deleted);
            Assert.Null(context.LastCutoff);
        }

        [Fact]
        public async Task DeleteOlderThanAsync_WithAnUnspecifiedKind_TreatsTheCutoffAsUtc()
        {
            var context = new ServiceContext();
            var cutoff = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Unspecified);

            await context.Service.DeleteOlderThanAsync(EnterpriseId, cutoff);

            Assert.Equal(cutoff, context.LastCutoff);
            Assert.Equal(DateTimeKind.Utc, context.LastCutoff!.Value.Kind);
        }

        [Fact]
        public async Task DeleteOlderThanAsync_WithALocalKind_ConvertsTheCutoffToUtc()
        {
            var context = new ServiceContext();
            var cutoff = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Local);

            await context.Service.DeleteOlderThanAsync(EnterpriseId, cutoff);

            Assert.Equal(cutoff.ToUniversalTime(), context.LastCutoff);
        }

        [Fact]
        public async Task DeleteOlderThanAsync_ReturnsWhatTheRepositoryDeleted()
        {
            var context = new ServiceContext(deletedRows: 17);

            Assert.Equal(17, await context.Service.DeleteOlderThanAsync(EnterpriseId, DateTime.UtcNow));
        }

        #endregion

        /// <summary>Records what the service handed the repository, which is all it does.</summary>
        private sealed class ServiceContext
        {
            public AuditTrailService Service { get; }

            public Guid LastEnterpriseId { get; private set; }
            public bool LastDescending { get; private set; }
            public int LastPageNumber { get; private set; }
            public int LastPageSize { get; private set; }
            public AuditTrailFilterDTO? LastFilter { get; private set; }
            public DateTime? LastCutoff { get; private set; }

            public ServiceContext(int deletedRows = 0)
            {
                var repository = new Mock<IAuditTrailRepository>();

                repository
                    .Setup(x => x.GetAllAsyncFiltering(
                        It.IsAny<Guid>(),
                        It.IsAny<bool>(),
                        It.IsAny<int?>(),
                        It.IsAny<int?>(),
                        It.IsAny<AuditTrailFilterDTO?>()))
                    .ReturnsAsync((Guid enterpriseId, bool descending, int? pageNumber, int? pageSize, AuditTrailFilterDTO? filter) =>
                    {
                        LastEnterpriseId = enterpriseId;
                        LastDescending = descending;
                        LastPageNumber = pageNumber!.Value;
                        LastPageSize = pageSize!.Value;
                        LastFilter = filter;
                        return (Enumerable.Empty<AuditTrail>(), 0);
                    });

                repository
                    .Setup(x => x.DeleteOlderThanAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((Guid _, DateTime cutoff, CancellationToken _) =>
                    {
                        LastCutoff = cutoff;
                        return deletedRows;
                    });

                Service = new AuditTrailService(repository.Object);
            }
        }
    }
}
