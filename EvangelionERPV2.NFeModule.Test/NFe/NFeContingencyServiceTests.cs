using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Application.Services;
using EvangelionERPV2.NFeModule.Domain.Interface;
using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    /// <summary>
    /// The sweeper that sends off-line notes after the fact.
    /// </summary>
    public class NFeContingencyServiceTests
    {
        private const string SignedXml = "<enviNFe><NFe /></enviNFe>";

        [Fact]
        public async Task AnOfflineNote_IsTransmittedFromWhatWasStored()
        {
            var context = new SweepContext(Contingency());
            context.Provider
                .Setup(x => x.TransmitSignedAsync(It.IsAny<string>(), SignedXml, It.IsAny<EnterpriseFiscalSettings>(),
                    It.IsAny<NFeDocumentType>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NFeProviderResult { Status = NFeStatus.Authorized, Protocol = "PROT-1", Cstat = "100" });

            var transmitted = await context.Service.TransmitPendingAsync();

            Assert.Equal(1, transmitted);
            Assert.Equal(NFeStatus.Authorized, context.Document.Status);
        }

        [Fact]
        public async Task ANoteWithAnUnknownOutcome_IsNeverRetransmitted()
        {
            var document = Contingency();
            document.TransmittedAtUtc = DateTime.UtcNow.AddMinutes(-30);

            var context = new SweepContext(document);

            var transmitted = await context.Service.TransmitPendingAsync();

            Assert.Equal(0, transmitted);
            context.Provider.Verify(x => x.TransmitSignedAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<EnterpriseFiscalSettings>(), It.IsAny<NFeDocumentType>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ANoteWithNoStoredXml_IsReportedRatherThanInvented()
        {
            var document = Contingency();
            document.XmlContent = string.Empty;

            var context = new SweepContext(document);

            Assert.Equal(0, await context.Service.TransmitPendingAsync());
            context.Provider.Verify(x => x.TransmitSignedAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<EnterpriseFiscalSettings>(), It.IsAny<NFeDocumentType>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task PastTheDeadline_ItKeepsTrying()
        {
            var document = Contingency();
            document.ContingencyEnteredAt = DateTime.UtcNow.AddHours(-72);

            var context = new SweepContext(document);
            context.Provider
                .Setup(x => x.TransmitSignedAsync(It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<EnterpriseFiscalSettings>(), It.IsAny<NFeDocumentType>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NFeProviderResult { Status = NFeStatus.Authorized, Cstat = "100" });

            Assert.Equal(1, await context.Service.TransmitPendingAsync());
        }

        [Fact]
        public async Task ARejectedNote_CountsItsAttemptAndIsNotTreatedAsSent()
        {
            var context = new SweepContext(Contingency());
            context.Provider
                .Setup(x => x.TransmitSignedAsync(It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<EnterpriseFiscalSettings>(), It.IsAny<NFeDocumentType>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NFeProviderResult { Status = NFeStatus.Rejected, Cstat = "539" });

            Assert.Equal(0, await context.Service.TransmitPendingAsync());
            Assert.Equal(1, context.Document.AttemptCount);
        }

        [Fact]
        public async Task OneBadNote_DoesNotStopTheSweep()
        {
            var first = Contingency();
            var second = Contingency();

            var context = new SweepContext(first, second);
            context.Provider
                .SetupSequence(x => x.TransmitSignedAsync(It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<EnterpriseFiscalSettings>(), It.IsAny<NFeDocumentType>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"))
                .ReturnsAsync(new NFeProviderResult { Status = NFeStatus.Authorized, Cstat = "100" });

            Assert.Equal(1, await context.Service.TransmitPendingAsync());
        }

        [Fact]
        public async Task WithIssuanceDisabled_NothingIsSwept()
        {
            var context = new SweepContext(Contingency()) { Enabled = false };

            Assert.Equal(0, await context.Rebuild().TransmitPendingAsync());
        }

        private static NFeDocument Contingency() => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = Guid.NewGuid(),
            AccessKey = "43250811222333000181650010000000071123456780",
            Type = NFeDocumentType.NFCe,
            Status = NFeStatus.Error,
            TipoEmissao = 9,
            ContingencyEnteredAt = DateTime.UtcNow.AddMinutes(-20),
            ContingencyJustification = "SEFAZ inacessivel no momento da venda",
            XmlContent = SignedXml,
            TransmittedAtUtc = null,
            IsActive = true
        };

        private sealed class SweepContext
        {
            private readonly NFeDocument[] _documents;

            public Mock<INFeProvider> Provider { get; } = new();
            public NFeDocument Document => _documents[0];
            public bool Enabled { get; init; } = true;
            public NFeContingencyService Service { get; }

            public SweepContext(params NFeDocument[] documents)
            {
                _documents = documents;
                Service = Build();
            }

            public NFeContingencyService Rebuild() => Build();

            private NFeContingencyService Build()
            {
                var repository = new Mock<IRepository<NFeDocument>>();
                repository.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

                var custom = new Mock<INFeRepository<NFeDocument>>();
                custom.Setup(x => x.GetUntransmittedContingencyAsync(It.IsAny<int>()))
                    .ReturnsAsync(_documents);

                var fiscalSettings = new Mock<INFeFiscalSettingsService>();
                fiscalSettings.Setup(x => x.GetSettingsAsync(It.IsAny<Guid>()))
                    .ReturnsAsync(new EnterpriseFiscalSettings());

                return new NFeContingencyService(
                    repository.Object,
                    custom.Object,
                    Provider.Object,
                    fiscalSettings.Object,
                    Options.Create(new NFeSettings { Enabled = Enabled }));
            }
        }
    }
}
