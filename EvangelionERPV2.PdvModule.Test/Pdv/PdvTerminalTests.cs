using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.PdvModule.Test.Pdv;

/// <summary>
/// The credential that proves a caller is a till we enrolled. It authorises nothing on its own —
/// every permission still comes from the operator's own token — but it is what lets a terminal
/// reach a login endpoint that has no reCAPTCHA, so it has to be stored like a password.
/// </summary>
public class PdvTerminalTests
{
    private const string Credential = "a-very-secret-terminal-credential";

    [Fact]
    public void Verifier_DoesNotContainThePlaintextCredential()
    {
        var terminal = PdvTerminal.Enroll(Guid.NewGuid(), "CAIXA-01", Credential);

        Assert.DoesNotContain(Credential, terminal.CredentialVerifier);
        Assert.True(terminal.Verify(Credential));
    }

    [Fact]
    public void Verify_RejectsTheWrongCredential()
    {
        var terminal = PdvTerminal.Enroll(Guid.NewGuid(), "CAIXA-01", Credential);

        Assert.False(terminal.Verify("something else"));
    }

    [Fact]
    public void RevokedTerminal_NeverVerifies_EvenWithTheRightCredential()
    {
        var terminal = PdvTerminal.Enroll(Guid.NewGuid(), "CAIXA-01", Credential);

        terminal.IsRevoked = true;

        Assert.False(terminal.Verify(Credential));
    }

    [Fact]
    public void TwoEnrollmentsOfTheSameCredential_ProduceDifferentVerifiers()
    {
        var first = PdvTerminal.Enroll(Guid.NewGuid(), "CAIXA-01", Credential);
        var second = PdvTerminal.Enroll(Guid.NewGuid(), "CAIXA-02", Credential);

        Assert.NotEqual(first.CredentialVerifier, second.CredentialVerifier);
    }

    [Fact]
    public void Verify_ReturnsFalse_ForAMalformedVerifier()
    {
        var terminal = new PdvTerminal { CredentialVerifier = "not-a-verifier" };

        Assert.False(terminal.Verify(Credential));
    }
}
