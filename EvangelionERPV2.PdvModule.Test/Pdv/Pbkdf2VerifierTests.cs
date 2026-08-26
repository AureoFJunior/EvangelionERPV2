using EvangelionERPV2.Shared.Security;

namespace EvangelionERPV2.PdvModule.Test.Pdv;

/// <summary>
/// The verifier behind both till secrets: a terminal's credential and a supervisor's override PIN.
/// </summary>
/// <remarks>
/// A PIN verifier is handed to the terminal on purpose — an override has to be checkable with the
/// network down. That is only safe because a verifier is not a PIN: 210,000 PBKDF2 iterations is
/// what stands between a stolen terminal database and six guessable digits.
/// </remarks>
public class Pbkdf2VerifierTests
{
    [Fact]
    public void Verifier_RoundTripsWithoutStoringTheSecret()
    {
        const string pin = "482913";

        var verifier = Pbkdf2Verifier.Create(pin);

        Assert.DoesNotContain(pin, verifier);
        Assert.True(Pbkdf2Verifier.Verify(pin, verifier));
        Assert.False(Pbkdf2Verifier.Verify("482914", verifier));
    }

    [Fact]
    public void Create_SaltsEachVerifier()
    {
        // Two supervisors picking the same PIN must not produce the same row: one leaked verifier
        // would otherwise name everyone who shares that PIN.
        Assert.NotEqual(Pbkdf2Verifier.Create("482913"), Pbkdf2Verifier.Create("482913"));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForAMalformedVerifier()
    {
        Assert.False(Pbkdf2Verifier.Verify("482913", "not-a-verifier"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Verify_ReturnsFalse_ForAnEmptySecret(string? secret)
    {
        Assert.False(Pbkdf2Verifier.Verify(secret, Pbkdf2Verifier.Create("482913")));
    }
}
