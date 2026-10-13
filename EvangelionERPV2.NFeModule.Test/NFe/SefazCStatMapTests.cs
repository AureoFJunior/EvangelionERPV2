using EvangelionERPV2.NFeModule.Infra.Sefaz;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    public class SefazCStatMapTests
    {
        [Theory]
        [InlineData(100)]
        [InlineData(150)]
        public void Authorization_AuthorizedCodes_MapToAuthorized(int cStat)
        {
            var verdict = SefazCStatMap.ForAuthorization(cStat);

            Assert.Equal(NFeStatus.Authorized, verdict.Status);
            Assert.False(verdict.IsTransient);
            Assert.False(verdict.NeverTransmitted);
        }

        [Theory]
        [InlineData(110)]
        [InlineData(301)]
        [InlineData(302)]
        [InlineData(303)]
        public void Authorization_DenegadoCodes_MapToDenied(int cStat) =>
            Assert.Equal(NFeStatus.Denied, SefazCStatMap.ForAuthorization(cStat).Status);

        /// <summary>
        /// The expensive mistake this map exists to prevent: recording an autorizador outage as a
        /// rejection burns the allocated number permanently, and only inutilização accounts for it.
        /// </summary>
        [Theory]
        [InlineData(105)]
        [InlineData(108)]
        [InlineData(109)]
        [InlineData(999)]
        public void Authorization_TransientCodes_ChangeNothingAndAreRetryable(int cStat)
        {
            var verdict = SefazCStatMap.ForAuthorization(cStat);

            Assert.Equal(NFeStatus.Pending, verdict.Status);
            Assert.True(verdict.IsTransient);
            Assert.NotEqual(NFeStatus.Rejected, verdict.Status);

            Assert.False(verdict.NeverTransmitted);
        }

        [Fact]
        public void Authorization_UnknownCode_IsRejected()
        {
            var verdict = SefazCStatMap.ForAuthorization(539);

            Assert.Equal(NFeStatus.Rejected, verdict.Status);
            Assert.False(verdict.IsTransient);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(150)]
        public void Consult_AuthorizedCodes_MapToAuthorized(int cStat) =>
            Assert.Equal(NFeStatus.Authorized, SefazCStatMap.ForConsult(cStat).Status);

        /// <summary>
        /// A consult is how an already-homologated cancellation comes back. Reading it as a
        /// rejection would overwrite a cancelled note with a burnt-number verdict.
        /// </summary>
        [Theory]
        [InlineData(101)]
        [InlineData(151)]
        [InlineData(135)]
        [InlineData(155)]
        public void Consult_CancellationCodes_MapToCancelled(int cStat) =>
            Assert.Equal(NFeStatus.Cancelled, SefazCStatMap.ForConsult(cStat).Status);

        /// <summary>
        /// 217 is the only answer that releases a number: SEFAZ has never seen the chave, so the
        /// write-ahead marker notwithstanding, nothing arrived and the same number may be resent.
        /// </summary>
        [Fact]
        public void Consult_NotInDatabase_ReleasesTheNumberForRetry()
        {
            var verdict = SefazCStatMap.ForConsult(217);

            Assert.Equal(NFeStatus.Error, verdict.Status);
            Assert.True(verdict.NeverTransmitted);
            Assert.False(verdict.IsTransient);
        }

        [Fact]
        public void Consult_Denegado_IsTerminalAndNotRetryable()
        {
            var verdict = SefazCStatMap.ForConsult(110);

            Assert.Equal(NFeStatus.Denied, verdict.Status);
            Assert.False(verdict.NeverTransmitted);
        }

        [Theory]
        [InlineData(135)]
        [InlineData(155)]
        public void Cancellation_HomologatedCodes_MapToCancelled(int cStat)
        {
            var verdict = SefazCStatMap.ForCancellation(cStat);

            Assert.Equal(NFeStatus.Cancelled, verdict.Status);
            Assert.False(verdict.IsTransient);
        }

        /// <summary>
        /// The cancel path commits CancelRequestedAt before the round trip, so a retry after a lost
        /// answer gets "já registrado" back. Calling that a refusal would leave a note cancelled at
        /// SEFAZ recorded as Authorized — the lie the whole cancel rework exists to prevent.
        /// </summary>
        [Fact]
        public void Cancellation_DuplicateEvent_IsAlreadyCancelled() =>
            Assert.Equal(NFeStatus.Cancelled, SefazCStatMap.ForCancellation(573).Status);

        /// <summary>
        /// 501 (prazo vencido) refuses the cancellation, and 100 is not a cancellation answer at
        /// all. The note stays authorized with its tax liability real, so nothing may write Cancelled.
        /// </summary>
        [Theory]
        [InlineData(501)]
        [InlineData(100)]
        public void Cancellation_AnythingElse_IsNeverCancelled(int cStat) =>
            Assert.NotEqual(NFeStatus.Cancelled, SefazCStatMap.ForCancellation(cStat).Status);

        [Fact]
        public void Cancellation_ServiceOutage_IsTransientRatherThanARefusal()
        {
            var verdict = SefazCStatMap.ForCancellation(108);

            Assert.True(verdict.IsTransient);
            Assert.Equal(NFeStatus.Pending, verdict.Status);
        }

        [Fact]
        public void IsServiceInOperation_IsTrueOnlyFor107()
        {
            Assert.True(SefazCStatMap.IsServiceInOperation(SefazCStatMap.ServicoEmOperacao));
            Assert.False(SefazCStatMap.IsServiceInOperation(108));
            Assert.False(SefazCStatMap.IsServiceInOperation(109));
            Assert.False(SefazCStatMap.IsServiceInOperation(999));
        }
    }
}
