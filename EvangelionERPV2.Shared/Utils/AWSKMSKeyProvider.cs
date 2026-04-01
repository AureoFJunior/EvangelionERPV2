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

            var secretValueResponse = _secretsManager.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = secretName.Split(":")?[0] ?? string.Empty
            }).GetAwaiter().GetResult();

            string secretString = secretValueResponse.SecretString ?? string.Empty;
            if (string.IsNullOrWhiteSpace(secretString))
                return string.Empty;

            string keyIdentifier = secretString.Replace("'", string.Empty).Replace("\"", string.Empty);

            try
            {
                if (keyIdentifier.StartsWith("{") && keyIdentifier.Contains(":"))
                {
                    var keyValue = keyIdentifier.Trim('{', '}').Split(':', 2);
                    if (keyValue.Length == 2)
                    {
                        return keyValue[1];
                    }
                }
                return keyIdentifier;
            }
            catch (JsonReaderException ex)
            {
                throw new FormatException("The secret is not in a valid JSON format.", ex);
            }
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
