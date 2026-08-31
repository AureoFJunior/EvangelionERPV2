using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Application.Providers;
using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.NFeModule.Application.Services;
using EvangelionERPV2.NFeModule.Domain.Interface;
using EvangelionERPV2.NFeModule.Infra.Xml;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Options;
using Moq;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    /// <summary>
    /// The transmit-safe ordering from docs/nfe/HANDOFF.md §4: number and chave are allocated and
    /// committed, then the write-ahead marker is committed, and only then is anything transmitted.
    /// </summary>
    /// <remarks>
    /// These tests exist because the failure they guard against is invisible and expensive: a
    /// transmission whose outcome is unknown, retried as if it had never happened, emits the note
    /// twice with real tax liability. Everything asserted here is state that must be durable
    /// <em>before</em> the provider is called, so the assertions are about ordering, not values.
    /// </remarks>
    public class NFeIssuanceWriteAheadTests
    {
        private const short NFeModel = 55;
        private const byte HomologationEnvironment = 2;
        private const int AllocatedNumber = 7;

        [Fact]
        public async Task IssueAsync_AllocatesNumberAndChaveBeforeTransmitting()
        {
            var context = new IssuanceContext();
            NFeDocument? documentSeenByProvider = null;

            context.Provider
                .Setup(x => x.IssueAsync(It.IsAny<NFeIssueContext>(), It.IsAny<CancellationToken>()))
                .Callback(() => documentSeenByProvider = Clone(context.Persisted!))
                .ReturnsAsync(new NFeProviderResult
                {
                    Protocol = "PROT-1",
                    XmlContent = "<xml />",
                    IssuedAt = DateTime.UtcNow,
                    TotalValue = 100,
                    Status = NFeStatus.Authorized
                });

            var result = await context.Service.IssueAsync(context.OrderId, context.EnterpriseId);

            Assert.NotNull(result);
            Assert.Equal(NFeStatus.Authorized, result!.Status);

            // The state as it stood at the moment of transmission — this is the assertion that
            // actually pins the ordering.
            Assert.NotNull(documentSeenByProvider);
            Assert.True(NFeAccessKeyBuilder.IsValid(documentSeenByProvider!.AccessKey));
            Assert.Equal(AllocatedNumber.ToString(), documentSeenByProvider.Number);
            Assert.Equal("1", documentSeenByProvider.Series);
            Assert.Equal("Homologation", documentSeenByProvider.Environment);
            Assert.NotNull(documentSeenByProvider.TransmittedAtUtc);
            Assert.Equal(NFeStatus.Pending, documentSeenByProvider.Status);

            // Claim, number + chave, marker, outcome.
            Assert.Equal(4, context.CommitCalls);
        }

        [Fact]
        public async Task IssueAsync_ChaveEmbedsTheAllocatedNumberSeriesAndEmitter()
        {
            var context = new IssuanceContext();
            context.SetupAuthorizingProvider();

            var result = await context.Service.IssueAsync(context.OrderId, context.EnterpriseId);

            var accessKey = result!.AccessKey;
            Assert.Equal(44, accessKey.Length);
            Assert.Equal("43", accessKey.Substring(0, 2));                      // cUF
            Assert.Equal("11222333000181", accessKey.Substring(6, 14));         // CNPJ
            Assert.Equal("55", accessKey.Substring(20, 2));                     // mod
            Assert.Equal("001", accessKey.Substring(22, 3));                    // serie
            Assert.Equal("000000007", accessKey.Substring(25, 9));              // nNF
            Assert.Equal("1", accessKey.Substring(34, 1));                      // tpEmis
        }

        [Fact]
        public async Task TransmitTimeout_LeavesPendingAndDoesNotReallocateNumber()
        {
            var context = new IssuanceContext();

            context.Provider
                .Setup(x => x.IssueAsync(It.IsAny<NFeIssueContext>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("SEFAZ did not answer."));

            await Assert.ThrowsAsync<TimeoutException>(
                () => context.Service.IssueAsync(context.OrderId, context.EnterpriseId));

            var document = context.Persisted!;

            // Not Error. Error would make the document eligible for ResetErroredDocumentForRetry,
            // which clears the chave and the marker — the two things that make a duplicate emission
            // detectable — and the next attempt would then transmit the same note a second time.
            Assert.Equal(NFeStatus.Pending, document.Status);
            Assert.NotNull(document.TransmittedAtUtc);
            Assert.True(document.IsOutcomeUnknown);
            Assert.True(NFeAccessKeyBuilder.IsValid(document.AccessKey));
            Assert.Equal(AllocatedNumber.ToString(), document.Number);

            context.VerifyAllocations(Times.Once());

            // The claim, the number and the marker are durable; the failure adds no fourth commit,
            // because there is nothing safe to record about an unknown outcome.
            Assert.Equal(3, context.CommitCalls);
        }

        [Fact]
        public async Task IssueAsync_WhenTenantHasNoFiscalProfile_FailsWithoutBurningANumber()
        {
            var context = new IssuanceContext();
            context.SetupAuthorizingProvider();
            context.FiscalSettings
                .Setup(x => x.GetSettingsAsync(context.EnterpriseId))
                .ReturnsAsync(new EnterpriseFiscalSettings { EnterpriseId = context.EnterpriseId });

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => context.Service.IssueAsync(context.OrderId, context.EnterpriseId));

            context.VerifyAllocations(Times.Never());
            Assert.Equal(NFeStatus.Error, context.Persisted!.Status);
            Assert.Null(context.Persisted.TransmittedAtUtc);
            context.Provider.Verify(
                x => x.IssueAsync(It.IsAny<NFeIssueContext>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task IssueAsync_WhenNumberSequenceIsMissing_LeavesDocumentRetryable()
        {
            var context = new IssuanceContext();
            context.SetupAuthorizingProvider();
            context.NFeRepositoryCustom
                .Setup(x => x.AllocateNextNumberAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<short>(),
                    It.IsAny<string>(),
                    It.IsAny<byte>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((int?)null);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => context.Service.IssueAsync(context.OrderId, context.EnterpriseId));

            // Error, not an unknown outcome: nothing was transmitted, so retrying is safe.
            Assert.Equal(NFeStatus.Error, context.Persisted!.Status);
            Assert.Null(context.Persisted.TransmittedAtUtc);
            Assert.False(context.Persisted.IsOutcomeUnknown);
        }

        [Fact]
        public async Task IssueAsync_RetryingANeverTransmittedDocument_ReusesTheAllocatedNumber()
        {
            var context = new IssuanceContext();
            context.SetupAuthorizingProvider();

            var accessKey = KeyFor(EmitterNow());
            context.Seed(ErroredDocument(context, accessKey));

            var result = await context.Service.IssueAsync(context.OrderId, context.EnterpriseId);

            // Nothing was ever transmitted, so the number it already holds is still unused. Burning
            // it and taking a new one would leave a gap that only inutilização can close.
            context.VerifyAllocations(Times.Never());
            Assert.Equal(accessKey, result!.AccessKey);
            Assert.Equal(AllocatedNumber.ToString(), result.Number);
            Assert.Equal(NFeStatus.Authorized, result.Status);
        }

        [Fact]
        public async Task IssueAsync_RetryingWithAChaveFromAnotherMonth_AllocatesAgain()
        {
            var context = new IssuanceContext();
            context.SetupAuthorizingProvider();

            // AAMM in the chave is the emission month. Retransmitting a stale one is a rejection,
            // so the allocation is only reused inside the month it was made.
            var staleKey = KeyFor(EmitterNow().AddMonths(-2));
            context.Seed(ErroredDocument(context, staleKey));

            var result = await context.Service.IssueAsync(context.OrderId, context.EnterpriseId);

            context.VerifyAllocations(Times.Once());
            Assert.NotEqual(staleKey, result!.AccessKey);
            Assert.True(NFeAccessKeyBuilder.IsValid(result.AccessKey));
        }

        private static NFeDocument ErroredDocument(IssuanceContext context, string accessKey) => new()
        {
            Id = Guid.NewGuid(),
            OrderId = context.OrderId,
            EnterpriseId = context.EnterpriseId,
            Type = NFeDocumentType.NFe,
            Model = NFeModel,
            Status = NFeStatus.Error,
            AccessKey = accessKey,
            Number = AllocatedNumber.ToString(),
            Series = "1",
            Environment = "Homologation",
            TransmittedAtUtc = null,
            IsActive = true
        };

        private static string KeyFor(DateTime emission) => NFeAccessKeyBuilder.Build(
            43,
            emission,
            "11222333000181",
            NFeModel,
            1,
            AllocatedNumber,
            1,
            NFeAccessKeyBuilder.GenerateCodigoNumerico(AllocatedNumber));

        /// <summary>Mirrors NFeService.EmitterNow — the chave's AAMM is in the emitter's timezone.</summary>
        private static DateTime EmitterNow()
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return DateTime.UtcNow.AddHours(-3);
            }
        }

        private static NFeDocument Clone(NFeDocument document) => new()
        {
            Id = document.Id,
            OrderId = document.OrderId,
            EnterpriseId = document.EnterpriseId,
            Status = document.Status,
            AccessKey = document.AccessKey,
            Number = document.Number,
            Series = document.Series,
            Environment = document.Environment,
            TransmittedAtUtc = document.TransmittedAtUtc
        };

        /// <summary>
        /// A tenant configured well enough to issue, with an in-memory stand-in for the document
        /// row so the assertions can read what was committed and when.
        /// </summary>
        internal sealed class IssuanceContext
        {
            public Guid OrderId { get; } = Guid.NewGuid();
            public Guid EnterpriseId { get; } = Guid.NewGuid();
            public Guid CustomerId { get; } = Guid.NewGuid();

            public Mock<IRepository<NFeDocument>> NFeRepository { get; } = new();
            public Mock<INFeRepository<NFeDocument>> NFeRepositoryCustom { get; } = new();
            public Mock<INFeProvider> Provider { get; } = new();
            public Mock<INFeFiscalSettingsService> FiscalSettings { get; } = new();

            public NFeDocument? Persisted { get; private set; }
            public int CommitCalls { get; private set; }

            public NFeService Service { get; }

            public IssuanceContext()
            {
                var orderRepository = new Mock<IRepository<Order>>();
                var customerRepository = new Mock<IRepository<Customer>>();
                var enterpriseRepository = new Mock<IRepository<Enterprise>>();

                orderRepository
                    .Setup(x => x.GetByIdAsync(OrderId))
                    .ReturnsAsync(new Order
                    {
                        Id = OrderId,
                        EnterpriseId = EnterpriseId,
                        CustomerId = CustomerId,
                        TotalValue = 100
                    });

                customerRepository
                    .Setup(x => x.GetByIdAsync(CustomerId))
                    .ReturnsAsync(new Customer { Id = CustomerId, Name = "Customer" });

                enterpriseRepository
                    .Setup(x => x.GetByIdAsync(EnterpriseId))
                    .ReturnsAsync(new Enterprise { Id = EnterpriseId, Name = "Enterprise" });

                // The claim is read back on the existing-document branch, so it has to become
                // visible the moment it is created, exactly as a committed row would.
                NFeRepositoryCustom
                    .Setup(x => x.GetByOrderIdAsync(OrderId, It.IsAny<NFeDocumentType>()))
                    .ReturnsAsync(() => Persisted);

                // Model and series left open so the same harness serves modelo 55, NFC-e and the
                // contingency series. Tests that care assert the arguments explicitly.
                NFeRepositoryCustom
                    .Setup(x => x.AllocateNextNumberAsync(
                        EnterpriseId,
                        It.IsAny<short>(),
                        It.IsAny<string>(),
                        HomologationEnvironment,
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(AllocatedNumber);

                NFeRepository
                    .Setup(x => x.CreateAsync(It.IsAny<NFeDocument>()))
                    .Callback<NFeDocument>(document => Persisted = document)
                    .ReturnsAsync((NFeDocument document) => document);

                NFeRepository
                    .Setup(x => x.Update(It.IsAny<NFeDocument>()))
                    .Returns((NFeDocument document) => document);

                NFeRepository
                    .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                    .Callback(() => CommitCalls++)
                    .Returns(Task.CompletedTask);

                FiscalSettings
                    .Setup(x => x.GetSettingsAsync(EnterpriseId))
                    .ReturnsAsync(new EnterpriseFiscalSettings
                    {
                        EnterpriseId = EnterpriseId,
                        Cnpj = "11222333000181",
                        CodigoUfIbge = 43,
                        Environment = HomologationEnvironment,
                        NFeSeries = "1",
                        NFCeSeries = "1",
                        NFCeContingencySeries = "9"
                    });

                Service = new NFeService(
                    NFeRepository.Object,
                    NFeRepositoryCustom.Object,
                    orderRepository.Object,
                    customerRepository.Object,
                    enterpriseRepository.Object,
                    Provider.Object,
                    FiscalSettings.Object,
                    Options.Create(new NFeSettings { Enabled = true }));
            }

            /// <summary>Puts an existing row in front of the service, as a committed claim would be.</summary>
            public void Seed(NFeDocument document) => Persisted = document;

            public void SetupAuthorizingProvider()
            {
                Provider
                    .Setup(x => x.IssueAsync(It.IsAny<NFeIssueContext>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new NFeProviderResult
                    {
                        Protocol = "PROT-1",
                        XmlContent = "<xml />",
                        IssuedAt = DateTime.UtcNow,
                        TotalValue = 100,
                        Status = NFeStatus.Authorized
                    });
            }

            public void VerifyAllocations(Times times) =>
                NFeRepositoryCustom.Verify(
                    x => x.AllocateNextNumberAsync(
                        It.IsAny<Guid>(),
                        It.IsAny<short>(),
                        It.IsAny<string>(),
                        It.IsAny<byte>(),
                        It.IsAny<CancellationToken>()),
                    times);
        }
    }
}
