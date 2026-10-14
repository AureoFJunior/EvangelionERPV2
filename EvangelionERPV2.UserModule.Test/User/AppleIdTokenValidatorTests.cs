using EvangelionERPV2.UserModule.Application.Services;

namespace EvangelionERPV2.UserModule.Test.User
{
    // APL-01: only a token Apple signed, for one of our apps, for this nonce, still in date, is accepted.
    public class AppleIdTokenValidatorTests
    {
        private const string RawNonce = AppleTestTokens.RawNonce;
        private readonly AppleTestTokens _apple = new();
        private readonly AppleIdTokenValidator _validator;

        public AppleIdTokenValidatorTests() => _validator = _apple.Validator();

        [Fact]
        public async Task Validate_AGenuineToken_ReturnsTheAppleIdentity()
        {
            var identity = await _validator.ValidateAsync(_apple.Token(), RawNonce);

            Assert.NotNull(identity);
            Assert.Equal("001234.abcdef.0001", identity!.Subject);
            Assert.Equal("ana@example.com", identity.Email);
            Assert.True(identity.EmailVerified);
            Assert.False(identity.IsPrivateRelay);
        }

        [Fact]
        public async Task Validate_AcceptsEveryConfiguredAudience()
        {
            Assert.NotNull(await _validator.ValidateAsync(_apple.Token(audience: "com.nerv.erp.web"), RawNonce));
        }

        [Fact]
        public async Task Validate_ReadsBooleanClaimsAndPrivateRelay()
        {
            var token = _apple.Token(email: "x1y2@privaterelay.appleid.com", emailVerified: true,
                extra: new Dictionary<string, object> { ["is_private_email"] = "true" });

            var identity = await _validator.ValidateAsync(token, RawNonce);

            Assert.True(identity!.EmailVerified);
            Assert.True(identity.IsPrivateRelay);
        }

        [Fact]
        public async Task Validate_ReportsAnUnverifiedEmail()
        {
            var identity = await _validator.ValidateAsync(_apple.Token(emailVerified: "false"), RawNonce);

            Assert.False(identity!.EmailVerified);
        }

        [Fact]
        public async Task Validate_RejectsAnotherIssuer()
        {
            Assert.Null(await _validator.ValidateAsync(_apple.Token(issuer: "https://evil.example"), RawNonce));
        }

        [Fact]
        public async Task Validate_RejectsAnotherAppsAudience()
        {
            Assert.Null(await _validator.ValidateAsync(_apple.Token(audience: "com.someone.else"), RawNonce));
        }

        [Fact]
        public async Task Validate_RejectsAnExpiredToken()
        {
            Assert.Null(await _validator.ValidateAsync(_apple.Token(expires: DateTime.UtcNow.AddMinutes(-5)), RawNonce));
        }

        [Fact]
        public async Task Validate_RejectsATokenSignedByAKeyAppleDidNotPublish()
        {
            Assert.Null(await _validator.ValidateAsync(_apple.Token(key: AppleTestTokens.NewKey("apple-key-1")), RawNonce));
        }

        [Theory]
        [InlineData("another-raw-nonce-0123456789abcdef")]
        [InlineData("")]
        [InlineData(null)]
        public async Task Validate_RejectsANonceThatIsNotTheOneWeSent(string? rawNonce)
        {
            Assert.Null(await _validator.ValidateAsync(_apple.Token(), rawNonce));
        }

        [Fact]
        public async Task Validate_RejectsATokenWhoseNonceIsTheRawValueInsteadOfItsHash()
        {
            Assert.Null(await _validator.ValidateAsync(_apple.Token(nonce: RawNonce), RawNonce));
        }

        [Fact]
        public async Task Validate_RejectsGarbage()
        {
            Assert.Null(await _validator.ValidateAsync("not.a.jwt", RawNonce));
            Assert.Null(await _validator.ValidateAsync(null, RawNonce));
        }

        [Fact]
        public async Task Validate_RefetchesKeysOnceWhenAppleRotatedThem()
        {
            var rotated = AppleTestTokens.NewKey("apple-key-2");
            _apple.Keys.NextKeys = new[] { _apple.Key, rotated };

            Assert.NotNull(await _validator.ValidateAsync(_apple.Token(key: rotated), RawNonce));
            Assert.Equal(1, _apple.Keys.Refreshes);
        }

        [Fact]
        public async Task Validate_WhenAppleKeysCannotBeFetched_SaysSoInsteadOfCallingTheTokenInvalid()
        {
            _apple.Keys.Fail = true;

            await Assert.ThrowsAsync<AppleKeysUnavailableException>(() => _validator.ValidateAsync(_apple.Token(), RawNonce));
        }

        [Fact]
        public async Task Validate_WithNoAudienceConfigured_RejectsEverything()
        {
            Assert.Null(await new AppleIdTokenValidator(_apple.Keys, Array.Empty<string>()).ValidateAsync(_apple.Token(), RawNonce));
        }
    }
}
