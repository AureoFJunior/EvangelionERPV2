using EvangelionERPV2.UserModule.Application.Services;
using System.Text;

namespace EvangelionERPV2.UserModule.Test.User
{
    public class TotpTests
    {
        private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

        [Theory]
        [InlineData(59L, "94287082")]
        [InlineData(1111111109L, "07081804")]
        [InlineData(1111111111L, "14050471")]
        [InlineData(1234567890L, "89005924")]
        [InlineData(2000000000L, "69279037")]
        [InlineData(20000000000L, "65353130")]
        public void Code_MatchesRfc6238Sha1Vectors(long unixSeconds, string expected)
        {
            Assert.Equal(expected, Totp.Code(RfcSecret, unixSeconds / Totp.StepSeconds, digits: 8));
        }

        [Fact]
        public void Code_DefaultsToSixDigits()
        {
            Assert.Equal("287082", Totp.Code(RfcSecret, 59 / Totp.StepSeconds));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1)]
        public void MatchStep_AcceptsOneStepOfSkewEitherWay(int offset)
        {
            const long current = 1000;
            var code = Totp.Code(RfcSecret, current + offset);

            Assert.Equal(current + offset, Totp.MatchStep(RfcSecret, code, current, lastUsedStep: null));
        }

        [Theory]
        [InlineData(-2)]
        [InlineData(2)]
        public void MatchStep_RejectsCodesOutsideTheWindow(int offset)
        {
            const long current = 1000;
            var code = Totp.Code(RfcSecret, current + offset);

            Assert.Null(Totp.MatchStep(RfcSecret, code, current, lastUsedStep: null));
        }

        [Fact]
        public void MatchStep_RejectsAStepAlreadyUsed()
        {
            const long current = 1000;
            var code = Totp.Code(RfcSecret, current);

            Assert.Null(Totp.MatchStep(RfcSecret, code, current, lastUsedStep: current));
        }

        [Theory]
        [InlineData("")]
        [InlineData("12345")]
        [InlineData("1234567")]
        [InlineData("12a456")]
        public void MatchStep_RejectsMalformedCodes(string code)
        {
            Assert.Null(Totp.MatchStep(RfcSecret, code, 1000, lastUsedStep: null));
        }

        [Fact]
        public void MatchStep_IgnoresSpaces()
        {
            var code = Totp.Code(RfcSecret, 1000);

            Assert.Equal(1000, Totp.MatchStep(RfcSecret, code[..3] + " " + code[3..], 1000, lastUsedStep: null));
        }

        [Fact]
        public void Base32_RoundTripsAndMatchesRfc4648()
        {
            Assert.Equal("MZXW6YTBOI", Totp.ToBase32(Encoding.ASCII.GetBytes("foobar")).TrimEnd('='));
            var secret = Totp.NewSecret();
            Assert.Equal(20, secret.Length);
            Assert.Equal(secret, Totp.FromBase32(Totp.ToBase32(secret)));
        }

        [Fact]
        public void RecoveryCode_IsTenBase32CharsShownAsTwoGroups()
        {
            var code = Totp.NewRecoveryCode();

            Assert.Matches("^[A-Z2-7]{5}-[A-Z2-7]{5}$", code);
            Assert.Equal(code.Replace("-", ""), Totp.NormalizeRecoveryCode(" " + code.ToLowerInvariant().Replace("-", "") + " "));
        }

        [Theory]
        [InlineData("ABCDE-FGH")]
        [InlineData("ABCDE-FGHI1")]
        [InlineData("ABCDE-FGHIJK")]
        public void NormalizeRecoveryCode_ReturnsNullWhenMalformed(string input)
        {
            Assert.Null(Totp.NormalizeRecoveryCode(input));
        }
    }
}
