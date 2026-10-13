namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>A login that proved the first factor and now needs a TOTP or recovery code.</summary>
    public sealed class MfaChallengeResponse
    {
        public bool MfaRequired { get; set; } = true;
        public string MfaToken { get; set; } = string.Empty;
    }

    public sealed class MfaSetupResponse
    {
        public string Secret { get; set; } = string.Empty;
        public string OtpauthUri { get; set; } = string.Empty;
    }

    public sealed class MfaRecoveryCodesResponse
    {
        public IReadOnlyList<string> RecoveryCodes { get; set; } = Array.Empty<string>();
    }
}
