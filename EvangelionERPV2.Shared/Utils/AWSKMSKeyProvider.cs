using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EvangelionERPV2.Shared.Utils
{
    public class AWSKMSKeyProvider
    {
        private readonly IAmazonSecretsManager _secretsManager;
        private readonly IConfiguration _configuration;

        public AWSKMSKeyProvider(IAmazonSecretsManager secretsManager, IConfiguration configuration)
        {
            _secretsManager = secretsManager;
            _configuration = configuration;
        }

        public string GetKMSKey(string secretName)
        {
            if (string.IsNullOrWhiteSpace(secretName))
            {
                return string.Empty;
            }

            const string plainPrefix = "plain:";
            if (secretName.StartsWith(plainPrefix, StringComparison.OrdinalIgnoreCase))
            {
                EnsurePlainSecretUsageIsAllowed();
                return secretName.Substring(plainPrefix.Length);
            }

            var (secretId, requestedKey) = SplitSecretReference(secretName);
            var secretValueResponse = _secretsManager.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = secretId
            }).GetAwaiter().GetResult();

            string secretString = secretValueResponse.SecretString ?? string.Empty;
            if (string.IsNullOrWhiteSpace(secretString))
                return string.Empty;

            return ResolveSecretValue(secretString, requestedKey);
        }

        private static (string SecretId, string? RequestedKey) SplitSecretReference(string secretName)
        {
            var normalizedSecretName = secretName.Trim();
            var separatorIndex = normalizedSecretName.LastIndexOf(':');
            if (separatorIndex <= 0 || separatorIndex == normalizedSecretName.Length - 1)
                return (normalizedSecretName, null);

            return (
                normalizedSecretName[..separatorIndex],
                normalizedSecretName[(separatorIndex + 1)..]);
        }

        private static string ResolveSecretValue(string secretString, string? requestedKey)
        {
            try
            {
                var token = JToken.Parse(secretString);
                if (token is JObject secretObject)
                {
                    var properties = secretObject.Properties().ToList();
                    if (!string.IsNullOrWhiteSpace(requestedKey))
                    {
                        var property = properties
                            .FirstOrDefault(x => x.Name.Equals(requestedKey, StringComparison.OrdinalIgnoreCase));

                        if (property != null)
                            return ConvertSecretTokenToString(property.Value);

                        return properties.Count == 1
                            ? ConvertSecretTokenToString(properties[0].Value)
                            : string.Empty;
                    }

                    return properties.Count == 1
                        ? ConvertSecretTokenToString(properties[0].Value)
                        : secretObject.ToString(Formatting.None);
                }

                return ResolvePlainSecretValue(ConvertSecretTokenToString(token), requestedKey);
            }
            catch (JsonReaderException)
            {
                return ResolvePlainSecretValue(secretString, requestedKey);
            }
        }

        private static string ResolvePlainSecretValue(string secretString, string? requestedKey)
        {
            if (TryResolvePlainKeyedSecret(secretString, requestedKey, out var resolvedValue))
                return resolvedValue;

            return secretString;
        }

        private static bool TryResolvePlainKeyedSecret(string secretString, string? requestedKey, out string resolvedValue)
        {
            resolvedValue = string.Empty;
            if (string.IsNullOrWhiteSpace(secretString) || string.IsNullOrWhiteSpace(requestedKey))
                return false;

            var trimmedSecret = secretString.Trim();
            var normalizedKey = requestedKey.Trim();
            var separators = new[] { ":", "=" };
            foreach (var candidate in EnumeratePlainSecretCandidates(trimmedSecret))
            {
                foreach (var separator in separators)
                {
                    var prefix = normalizedKey + separator;
                    if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedValue = candidate.Substring(prefix.Length).Trim();
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<string> EnumeratePlainSecretCandidates(string trimmedSecret)
        {
            yield return trimmedSecret;

            if (trimmedSecret.Length >= 2 &&
                trimmedSecret.StartsWith("{", StringComparison.Ordinal) &&
                trimmedSecret.EndsWith("}", StringComparison.Ordinal))
            {
                yield return trimmedSecret[1..^1].Trim();
            }
        }

        private static string ConvertSecretTokenToString(JToken token)
        {
            if (token.Type == JTokenType.String)
                return token.Value<string>() ?? string.Empty;

            return token is JObject or JArray
                ? token.ToString(Formatting.None)
                : token.ToString();
        }

        public async Task<Dictionary<string, string>> GetAllKMSKeysAsync()
        {
            var secretValueResponse = await _secretsManager.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = _configuration.GetSection("AWSSettings")["SecretName"]
            });

            var secretString = secretValueResponse.SecretString;
            if (string.IsNullOrWhiteSpace(secretString))
                return new Dictionary<string, string>();

            var secretJson = JObject.Parse(secretString);

            return secretJson.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        }

        public async Task<BasicAWSCredentials> GetAWSCredentialsAsync()
        {
            var environmentAccessKeyId = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
            var environmentSecretAccessKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
            if (!string.IsNullOrWhiteSpace(environmentAccessKeyId) && !string.IsNullOrWhiteSpace(environmentSecretAccessKey))
                return new BasicAWSCredentials(environmentAccessKeyId, environmentSecretAccessKey);

            var configuredSecretName = _configuration.GetSection("AWSSettings")["SecretName"] ?? string.Empty;
            if (TryGetPlainCredentials(configuredSecretName, out var plainCredentials) && plainCredentials != null)
            {
                EnsurePlainSecretUsageIsAllowed();
                return plainCredentials;
            }

            if (string.IsNullOrWhiteSpace(configuredSecretName))
                throw new InvalidOperationException("AWSSettings:SecretName is not configured.");

            var secretValueResponse = await _secretsManager.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = configuredSecretName
            });

            var secretString = secretValueResponse.SecretString;
            if (string.IsNullOrWhiteSpace(secretString))
                throw new InvalidOperationException("AWS secret is empty.");

            var secretJson = JObject.Parse(secretString);

            var accessKeyId = secretJson["access-key-id"]?.ToString();
            var secretAccessKey = secretJson["secret-access-key"]?.ToString();

            if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(secretAccessKey))
                throw new InvalidOperationException("AWS credentials are missing required values.");

            return new BasicAWSCredentials(accessKeyId, secretAccessKey);
        }

        private static bool TryGetPlainCredentials(string secretName, out BasicAWSCredentials? credentials)
        {
            credentials = null;

            const string plainPrefix = "plain:";
            if (string.IsNullOrWhiteSpace(secretName) || !secretName.StartsWith(plainPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var payload = secretName.Substring(plainPrefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(payload))
                return false;

            var pipeSeparatedValues = payload.Split('|', 2, StringSplitOptions.TrimEntries);
            if (pipeSeparatedValues.Length == 2 &&
                !string.IsNullOrWhiteSpace(pipeSeparatedValues[0]) &&
                !string.IsNullOrWhiteSpace(pipeSeparatedValues[1]))
            {
                credentials = new BasicAWSCredentials(pipeSeparatedValues[0], pipeSeparatedValues[1]);
                return true;
            }

            try
            {
                var secretJson = JObject.Parse(payload);
                var accessKeyId = secretJson["access-key-id"]?.ToString()
                    ?? secretJson["accessKeyId"]?.ToString()
                    ?? secretJson["AccessKeyId"]?.ToString();
                var secretAccessKey = secretJson["secret-access-key"]?.ToString()
                    ?? secretJson["secretAccessKey"]?.ToString()
                    ?? secretJson["SecretAccessKey"]?.ToString();

                if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(secretAccessKey))
                    return false;

                credentials = new BasicAWSCredentials(accessKeyId, secretAccessKey);
                return true;
            }
            catch (JsonReaderException)
            {
                return false;
            }
        }

        private static void EnsurePlainSecretUsageIsAllowed()
        {
            if (IsDevelopmentEnvironment())
                return;

            throw new InvalidOperationException("The plain secret prefix is only allowed in development environments.");
        }

        private static bool IsDevelopmentEnvironment()
        {
            var environmentName =
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? string.Empty;

            return environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase);
        }
    }
}
