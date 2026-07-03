using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    public class AWSKMSKeyProviderSecretResolutionTests
    {
        [Fact]
        public void GetKMSKey_WithNamedJsonSecret_ReturnsRequestedProperty()
        {
            var secretsManager = CreateSecretsManager(
                "evangelion/prd/selfapilogin",
                """{"log":"worker-user:worker-pass","other":"ignored"}""");
            var provider = new AWSKMSKeyProvider(secretsManager.Object, CreateConfiguration());

            var result = provider.GetKMSKey("evangelion/prd/selfapilogin:log");

            Assert.Equal("worker-user:worker-pass", result);
            secretsManager.Verify(
                x => x.GetSecretValueAsync(
                    It.Is<GetSecretValueRequest>(request => request.SecretId == "evangelion/prd/selfapilogin"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public void GetKMSKey_WithSinglePropertyJsonSecretAndDifferentRequestedKey_ReturnsOnlyPropertyValue()
        {
            var secretsManager = CreateSecretsManager(
                "evangelion/dev/encryptiontokenkey",
                """{"evangelion/dev/encryptiontokenkey":"jwt-token-key"}""");
            var provider = new AWSKMSKeyProvider(secretsManager.Object, CreateConfiguration());

            var result = provider.GetKMSKey("evangelion/dev/encryptiontokenkey:key");

            Assert.Equal("jwt-token-key", result);
        }

        [Fact]
        public void GetKMSKey_WithJsonCredentialsWithoutRequestedKey_ReturnsCompactJsonPayload()
        {
            var secretsManager = CreateSecretsManager(
                "evangelion/prd/selfapilogin",
                """{"username":"worker-user","password":"worker-pass"}""");
            var provider = new AWSKMSKeyProvider(secretsManager.Object, CreateConfiguration());

            var result = provider.GetKMSKey("evangelion/prd/selfapilogin");

            Assert.Equal("""{"username":"worker-user","password":"worker-pass"}""", result);
        }

        [Fact]
        public void GetKMSKey_WithPlainSecretAndRequestedKey_PreservesExistingPlainSecretBehavior()
        {
            var secretsManager = CreateSecretsManager(
                "evangelion/prd/encryptionkey",
                "plain-secret-value");
            var provider = new AWSKMSKeyProvider(secretsManager.Object, CreateConfiguration());

            var result = provider.GetKMSKey("evangelion/prd/encryptionkey:key");

            Assert.Equal("plain-secret-value", result);
        }

        [Fact]
        public void GetKMSKey_WithPlainKeyPrefixedSecret_ReturnsValueAfterRequestedKey()
        {
            var secretsManager = CreateSecretsManager(
                "evangelion/dev/rabbitmq",
                "uri:amqps://user:pass@example.rabbitmq.local:5671/%2f");
            var provider = new AWSKMSKeyProvider(secretsManager.Object, CreateConfiguration());

            var result = provider.GetKMSKey("evangelion/dev/rabbitmq:uri");

            Assert.Equal("amqps://user:pass@example.rabbitmq.local:5671/%2f", result);
        }

        [Fact]
        public void GetKMSKey_WithPlainEqualsPrefixedSecret_ReturnsValueAfterRequestedKey()
        {
            var secretsManager = CreateSecretsManager(
                "evangelion/dev/rabbitmq-host",
                "hostname=rabbitmq.example.local");
            var provider = new AWSKMSKeyProvider(secretsManager.Object, CreateConfiguration());

            var result = provider.GetKMSKey("evangelion/dev/rabbitmq-host:hostname");

            Assert.Equal("rabbitmq.example.local", result);
        }

        [Fact]
        public void GetKMSKey_WithJsonStringKeyPrefixedSecret_ReturnsValueAfterRequestedKey()
        {
            var secretsManager = CreateSecretsManager(
                "evangelion/dev/rabbitmq",
                "\"uri:amqps://user:pass@example.rabbitmq.local:5671/%2f\"");
            var provider = new AWSKMSKeyProvider(secretsManager.Object, CreateConfiguration());

            var result = provider.GetKMSKey("evangelion/dev/rabbitmq:uri");

            Assert.Equal("amqps://user:pass@example.rabbitmq.local:5671/%2f", result);
        }

        [Fact]
        public void GetKMSKey_WithBracedPlainKeyPrefixedSecret_ReturnsValueAfterRequestedKey()
        {
            var secretsManager = CreateSecretsManager(
                "evangelion/dev/rabbitmq",
                "{uri:amqps://user:pass@example.rabbitmq.local:5671/%2f}");
            var provider = new AWSKMSKeyProvider(secretsManager.Object, CreateConfiguration());

            var result = provider.GetKMSKey("evangelion/dev/rabbitmq:uri");

            Assert.Equal("amqps://user:pass@example.rabbitmq.local:5671/%2f", result);
        }

        private static Mock<IAmazonSecretsManager> CreateSecretsManager(string expectedSecretId, string secretString)
        {
            var secretsManager = new Mock<IAmazonSecretsManager>(MockBehavior.Strict);
            secretsManager
                .Setup(x => x.GetSecretValueAsync(
                    It.Is<GetSecretValueRequest>(request => request.SecretId == expectedSecretId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse
                {
                    SecretString = secretString
                });

            return secretsManager;
        }

        private static IConfiguration CreateConfiguration()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();
        }
    }
}
