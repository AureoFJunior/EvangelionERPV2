using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    /// <summary>
    /// The persisted enum has six values; the UI shows nine labels. These tests pin the
    /// derivation, because the distinction that matters most — "not sent yet" versus
    /// "sent, outcome unknown" — is invisible in the enum alone and demands opposite
    /// operator behaviour.
    /// </summary>
    public class NFeDocumentStateTests
    {
        private static readonly DateTime Moment = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

        #region DisplayStatus

        [Fact]
        public void ResolveDisplayStatus_PendingNeverTransmitted_IsProcessing()
        {
            var document = new NFeDocument { Status = NFeStatus.Pending, TransmittedAtUtc = null };

            Assert.Equal(NFeDisplayStatus.Processing, NFeDocumentDTO.ResolveDisplayStatus(document));
        }

        [Fact]
        public void ResolveDisplayStatus_PendingAfterTransmission_IsAwaitingSefaz()
        {
            var document = new NFeDocument { Status = NFeStatus.Pending, TransmittedAtUtc = Moment };

            Assert.Equal(NFeDisplayStatus.AwaitingSefaz, NFeDocumentDTO.ResolveDisplayStatus(document));
        }

        [Fact]
        public void ResolveDisplayStatus_AuthorizedWithCancelInFlight_IsCancellationRequested()
        {
            var document = new NFeDocument
            {
                Status = NFeStatus.Authorized,
                CancelRequestedAt = Moment,
                CancelledAtUtc = null
            };

            Assert.Equal(NFeDisplayStatus.CancellationRequested, NFeDocumentDTO.ResolveDisplayStatus(document));
        }

        [Fact]
        public void ResolveDisplayStatus_AuthorizedAfterRejectedCancel_IsAuthorizedAgain()
        {
            var document = new NFeDocument
            {
                Status = NFeStatus.Authorized,
                CancelRequestedAt = null,
                CancelCstat = "501"
            };

            Assert.Equal(NFeDisplayStatus.Authorized, NFeDocumentDTO.ResolveDisplayStatus(document));
        }

        [Theory]
        [InlineData(NFeStatus.Authorized, NFeDisplayStatus.Authorized)]
        [InlineData(NFeStatus.Cancelled, NFeDisplayStatus.Cancelled)]
        [InlineData(NFeStatus.Rejected, NFeDisplayStatus.Rejected)]
        [InlineData(NFeStatus.Denied, NFeDisplayStatus.Denied)]
        [InlineData(NFeStatus.Error, NFeDisplayStatus.Error)]
        public void ResolveDisplayStatus_MapsTerminalAndSimpleStates(NFeStatus status, NFeDisplayStatus expected)
        {
            Assert.Equal(expected, NFeDocumentDTO.ResolveDisplayStatus(new NFeDocument { Status = status }));
        }

        #endregion

        #region Invariant helpers

        [Fact]
        public void IsOutcomeUnknown_OnlyWhenPendingAndTransmitted()
        {
            Assert.True(new NFeDocument { Status = NFeStatus.Pending, TransmittedAtUtc = Moment }.IsOutcomeUnknown);
            Assert.False(new NFeDocument { Status = NFeStatus.Pending, TransmittedAtUtc = null }.IsOutcomeUnknown);
            Assert.False(new NFeDocument { Status = NFeStatus.Authorized, TransmittedAtUtc = Moment }.IsOutcomeUnknown);
        }

        [Theory]
        [InlineData(NFeStatus.Cancelled, true)]
        [InlineData(NFeStatus.Denied, true)]
        [InlineData(NFeStatus.Authorized, false)]
        [InlineData(NFeStatus.Rejected, false)]
        [InlineData(NFeStatus.Error, false)]
        [InlineData(NFeStatus.Pending, false)]
        public void IsTerminal_MarksOnlyCancelledAndDenied(NFeStatus status, bool expected)
        {
            Assert.Equal(expected, new NFeDocument { Status = status }.IsTerminal);
        }

        #endregion

        #region Enum stability

        [Fact]
        public void NFeStatus_KeepsExistingWireValues()
        {
            Assert.Equal(1, (int)NFeStatus.Pending);
            Assert.Equal(2, (int)NFeStatus.Authorized);
            Assert.Equal(3, (int)NFeStatus.Cancelled);
            Assert.Equal(4, (int)NFeStatus.Error);
            Assert.Equal(5, (int)NFeStatus.Rejected);
            Assert.Equal(6, (int)NFeStatus.Denied);
        }

        [Theory]
        [InlineData(NFeDocumentType.NFe, (short)55)]
        [InlineData(NFeDocumentType.NFCe, (short)65)]
        public void ModelFor_MapsTypeToSefazModel(NFeDocumentType type, short expected)
        {
            Assert.Equal(expected, NFeDocument.ModelFor(type));
        }

        #endregion

        #region Privacy

        [Fact]
        public void NFeDocumentDTO_DoesNotExposeAccessKeyDerivedFields()
        {
            var propertyNames = typeof(NFeDocumentDTO).GetProperties().Select(x => x.Name).ToArray();

            Assert.DoesNotContain("QrCode", propertyNames);
            Assert.DoesNotContain("UrlChave", propertyNames);
            Assert.DoesNotContain("DigestValue", propertyNames);
        }

        [Fact]
        public void NFeDocumentDTO_DoesNotSerializeXmlContent()
        {
            var dto = new NFeDocumentDTO
            {
                AccessKey = new string('1', 44),
                XmlContent = "<nfeProc><dest><CPF>52998224725</CPF></dest></nfeProc>"
            };

            var json = System.Text.Json.JsonSerializer.Serialize(dto);

            Assert.DoesNotContain("52998224725", json);
            Assert.DoesNotContain("nfeProc", json);
        }

        #endregion
    }
}
