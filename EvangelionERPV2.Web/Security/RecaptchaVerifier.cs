using System.Text.Json;
using System.Text.Json.Serialization;
using EvangelionERPV2.Shared.Utils;
using Serilog;

namespace EvangelionERPV2.Web.Security
{
    public enum RecaptchaVerificationStatus
    {
        Success,
        Missing,
        Failed
    }

    public sealed record RecaptchaVerificationResult(RecaptchaVerificationStatus Status)
    {
        public bool IsSuccess => Status == RecaptchaVerificationStatus.Success;

        public static readonly RecaptchaVerificationResult Success = new(RecaptchaVerificationStatus.Success);
        public static readonly RecaptchaVerificationResult Missing = new(RecaptchaVerificationStatus.Missing);
        public static readonly RecaptchaVerificationResult Failed = new(RecaptchaVerificationStatus.Failed);
    }

    public interface IRecaptchaVerifier
    {
        Task<RecaptchaVerificationResult> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Server-to-server verification of reCAPTCHA v2 Checkbox tokens against Google's siteverify endpoint.
    /// The secret key is resolved from the secret store (never returned to clients, never logged).
    /// Verification is fail-closed: any error or missing configuration rejects the request.
    /// </summary>
    public sealed class RecaptchaVerifier : IRecaptchaVerifier
    {
        private const string VerifyUrl = "https://www.google.com/recaptcha/api/siteverify";

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly AWSKMSKeyProvider _kmsProvider;

        public RecaptchaVerifier(HttpClient httpClient, IConfiguration configuration, AWSKMSKeyProvider kmsProvider)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _kmsProvider = kmsProvider;
        }

        public async Task<RecaptchaVerificationResult> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default)
        {
            var section = _configuration.GetSection("RecaptchaSettings");

            // Toggle lets local/dev environments and tests run without a live reCAPTCHA challenge.
            if (!section.GetValue<bool>("Enabled"))
                return RecaptchaVerificationResult.Success;

            if (string.IsNullOrWhiteSpace(token))
                return RecaptchaVerificationResult.Missing;

            var secret = await ResolveSecretAsync(section["SecretKey"], cancellationToken);
            if (string.IsNullOrWhiteSpace(secret))
            {
                Log.Logger.Error("reCAPTCHA secret key is not configured. Rejecting request (fail-closed).");
                return RecaptchaVerificationResult.Failed;
            }

            try
            {
                var form = new Dictionary<string, string>
                {
                    ["secret"] = secret,
                    ["response"] = token
                };

                if (!string.IsNullOrWhiteSpace(remoteIp))
                    form["remoteip"] = remoteIp;

                using var content = new FormUrlEncodedContent(form);
                using var response = await _httpClient.PostAsync(VerifyUrl, content, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Logger.Warning("reCAPTCHA siteverify returned non-success status. StatusCode={StatusCode}", (int)response.StatusCode);
                    return RecaptchaVerificationResult.Failed;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var verification = JsonSerializer.Deserialize<SiteVerifyResponse>(body);
                if (verification == null || !verification.Success)
                {
                    Log.Logger.Warning(
                        "reCAPTCHA verification failed. ErrorCodes={ErrorCodes}",
                        verification?.ErrorCodes != null ? string.Join(',', verification.ErrorCodes) : "none");
                    return RecaptchaVerificationResult.Failed;
                }

                if (!IsHostnameAllowed(section, verification.Hostname))
                {
                    Log.Logger.Warning("reCAPTCHA hostname is not allowed. Hostname={Hostname}", verification.Hostname);
                    return RecaptchaVerificationResult.Failed;
                }

                return RecaptchaVerificationResult.Success;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("reCAPTCHA verification request failed. ErrorType={ErrorType}", ex.GetType().Name);
                return RecaptchaVerificationResult.Failed;
            }
        }

        private async Task<string> ResolveSecretAsync(string? secretName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(secretName))
                return string.Empty;

            return await _kmsProvider.GetKMSKeyAsync(secretName, cancellationToken);
        }

        private static bool IsHostnameAllowed(IConfigurationSection section, string? hostname)
        {
            var allowedHostnames = section.GetSection("AllowedHostnames")
                .GetChildren()
                .Select(child => child.Value?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (allowedHostnames.Count == 0)
                return true;

            return allowedHostnames.Any(allowed => string.Equals(allowed, hostname?.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private sealed class SiteVerifyResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("hostname")]
            public string? Hostname { get; set; }

            [JsonPropertyName("challenge_ts")]
            public string? ChallengeTimestamp { get; set; }

            [JsonPropertyName("error-codes")]
            public string[]? ErrorCodes { get; set; }
        }
    }
}
