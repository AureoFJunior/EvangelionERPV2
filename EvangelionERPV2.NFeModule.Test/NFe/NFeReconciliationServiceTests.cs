using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Application.Providers;
using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.NFeModule.Application.Services;
using EvangelionERPV2.NFeModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Options;
using Moq;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    /// <summary>
    /// The reconciler is the only owner of a document the issue path could not finish, and the two
    /// states it handles have opposite remedies: an unknown outcome may only ever be consulted,
    /// while a document that provably never left may be sent again with the same number. Confusing
    /// them emits the note twice, so every test here is about which branch was taken.
    /// </summary>
    public class NFeReconciliationServiceTests
    {
        private const string AccessKey = "43250811222333000181550010000000071123456780";

        [Fact]
        public async Task Reconcile_WhenSefazAuthorized_AdoptsTheOutcome()
        {
            var context = new ReconciliationContext();
            var document = context.Stale(TransmittedDocument());

            var authorizedAt = new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);
            context.Provider
                .Setup(x => x.ConsultAsync(document.AccessKey, It.IsAny<EnterpriseFiscalSettings>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NFeProviderResult
                {
                    Status = NFeStatus.Authorized,
                    Cstat = "100",
                    XMotivo = "Autorizado o uso da NF-e",
                    Protocol = "135260000000001",
                    XmlContent = "<protNFe />",
                    IssuedAt = authorizedAt
                });

            var resolved = await context.Service.ReconcileAsync();

            Assert.Equal(1, resolved);
            Assert.Equal(NFeStatus.Authorized, document.Status);
            Assert.Equal("100", document.Cstat);
            Assert.Equal("135260000000001", document.Protocol);
            Assert.Equal(authorizedAt, document.AuthorizedAtUtc);
            Assert.Equal(1, document.AttemptCount);
            Assert.Equal(1, context.CommitCalls);
        }

        [Fact]
        public async Task Reconcile_WhenSefazStillHasNoAnswer_LeavesItPendingAndCountsTheAttempt()
        {
            var context = new ReconciliationContext();
            var document = context.Stale(TransmittedDocument());
            var transmittedAt = document.TransmittedAtUtc;

            context.Provider
                .Setup(x => x.ConsultAsync(It.IsAny<string>(), It.IsAny<EnterpriseFiscalSettings>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NFeProviderResult { Status = NFeStatus.Pending });

            var resolved = await context.Service.ReconcileAsync();

            Assert.Equal(0, resolved);
            Assert.Equal(NFeStatus.Pending, document.Status);
            Assert.True(document.IsOutcomeUnknown);

            // The marker and the chave survive: they are what the next consult is made against, and
            // clearing either would turn an unknown outcome into a retransmission.
            Assert.Equal(transmittedAt, document.TransmittedAtUtc);
            Assert.Equal(AccessKey, document.AccessKey);

            // Counted, so a note SEFAZ never answers for eventually parks for a human instead of
            // being consulted forever.
            Assert.Equal(1, document.AttemptCount);
        }

        [Fact]
        public async Task Reconcile_NeverTransmitted_ReleasesForRetryKeepingTheNumber()
        {
            var context = new ReconciliationContext();
            var document = context.Stale(new NFeDocument
            {
                Id = Guid.NewGuid(),
                Status = NFeStatus.Pending,
                AccessKey = AccessKey,
                Number = "7",
                Series = "1",
                TransmittedAtUtc = null
            });

            var resolved = await context.Service.ReconcileAsync();

            Assert.Equal(1, resolved);
            Assert.Equal(NFeStatus.Error, document.Status);
            Assert.Equal("7", document.Number);
            Assert.Equal(AccessKey, document.AccessKey);
            Assert.Null(document.TransmittedAtUtc);

            // Never consulted: there is nothing at SEFAZ to ask about.
            context.Provider.Verify(
                x => x.ConsultAsync(It.IsAny<string>(), It.IsAny<EnterpriseFiscalSettings>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Reconcile_WhenOneDocumentThrows_StillProcessesTheRest()
        {
            var context = new ReconciliationContext();
            // Distinct keys: the branch taken has to be attributable to one document, not to the
            // order the sweep happened to visit them in.
            var failing = TransmittedDocument("43250811222333000181550010000000081123456781");
            var succeeding = TransmittedDocument();
            context.Stale(failing, succeeding);

            context.Provider
                .Setup(x => x.ConsultAsync(failing.AccessKey, It.IsAny<EnterpriseFiscalSettings>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("SEFAZ did not answer."));
            context.Provider
                .Setup(x => x.ConsultAsync(succeeding.AccessKey, It.IsAny<EnterpriseFiscalSettings>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NFeProviderResult { Status = NFeStatus.Rejected, Cstat = "539" });

            var resolved = await context.Service.ReconcileAsync();

            Assert.Equal(1, resolved);
            Assert.Equal(NFeStatus.Pending, failing.Status);
            Assert.Equal(NFeStatus.Rejected, succeeding.Status);
        }

        [Fact]
        public async Task Reconcile_WhenIssuanceIsDisabled_DoesNotTouchAnything()
        {
            var context = new ReconciliationContext(enabled: false);
            context.Stale(TransmittedDocument());

            var resolved = await context.Service.ReconcileAsync();

            Assert.Equal(0, resolved);
            context.NFeRepositoryCustom.Verify(
                x => x.GetStalePendingAsync(It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<int>()),
                Times.Never);
        }

        private static NFeDocument TransmittedDocument(string accessKey = AccessKey) => new()
        {
            Id = Guid.NewGuid(),
            Status = NFeStatus.Pending,
            AccessKey = accessKey,
            Number = "7",
            Series = "1",
            TransmittedAtUtc = DateTime.UtcNow.AddHours(-1)
        };

        private sealed class ReconciliationContext
        {
            public Mock<IRepository<NFeDocument>> NFeRepository { get; } = new();
            public Mock<INFeRepository<NFeDocument>> NFeRepositoryCustom { get; } = new();
            public Mock<INFeProvider> Provider { get; } = new();
            public Mock<INFeFiscalSettingsService> FiscalSettings { get; } = new();

            public int CommitCalls { get; private set; }
            public NFeReconciliationService Service { get; }

            public ReconciliationContext(bool enabled = true)
            {
                NFeRepository
                    .Setup(x => x.Update(It.IsAny<NFeDocument>()))
                    .Returns((NFeDocument document) => document);

                NFeRepository
                    .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                    .Callback(() => CommitCalls++)
                    .Returns(Task.CompletedTask);

                FiscalSettings
                    .Setup(x => x.GetSettingsAsync(It.IsAny<Guid>()))
                    .ReturnsAsync(new EnterpriseFiscalSettings { Cnpj = "11222333000181", CodigoUfIbge = 43 });

                Service = new NFeReconciliationService(
                    NFeRepository.Object,
                    NFeRepositoryCustom.Object,
                    Provider.Object,
                    FiscalSettings.Object,
                    Options.Create(new NFeSettings { Enabled = enabled }));
            }

            /// <summary>Makes the given documents the result of the next sweep.</summary>
            public NFeDocument Stale(params NFeDocument[] documents)
            {
                NFeRepositoryCustom
                    .Setup(x => x.GetStalePendingAsync(It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<int>()))
                    .ReturnsAsync(documents);

                return documents[0];
            }
        }
    }
}
