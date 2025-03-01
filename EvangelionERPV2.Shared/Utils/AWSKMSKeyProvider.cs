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
            var secretValueResponse = _secretsManager.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = secretName.Split(":")?[0] ?? string.Empty
            }).GetAwaiter().GetResult();

            string secretString = secretValueResponse.SecretString;
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
                return string.Empty;
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
            var secretJson = JObject.Parse(secretString);

            return secretJson.ToObject<Dictionary<string, string>>();
        }

        public async Task<BasicAWSCredentials> GetAWSCredentialsAsync()
        {
            var secretValueResponse = await _secretsManager.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = _configuration.GetSection("AWSSettings")["SecretName"]
            });

            var secretString = secretValueResponse.SecretString;
            var secretJson = JObject.Parse(secretString);

            var accessKeyId = secretJson["access-key-id"].ToString();
            var secretAccessKey = secretJson["secret-access-key"].ToString();

            return new BasicAWSCredentials(accessKeyId, secretAccessKey);
        }
    }
}
