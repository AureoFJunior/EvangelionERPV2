using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EvangelionERPV2.UserModule.Test.User
{
    /// <summary>
    /// Classes that touch process-wide statics — SharedFunctions' encryption key, the global Serilog
    /// logger — must not run in parallel with each other.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class SharedFunctionsKeyCollection
    {
        public const string Name = "SharedFunctions encryption key";

        private SharedFunctionsKeyCollection() { }

        /// <summary>Points SharedFunctions at a fixed all-zero key, so tokens can be signed in a test.</summary>
        public static IConfiguration EnsureInitialized()
        {
            const string secretName = "shared-functions-tests-key";
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = secretName,
                    ["Encryption:TokenKey"] = secretName
                })
                .Build();

            var secretsManager = new Mock<IAmazonSecretsManager>(MockBehavior.Strict);
            secretsManager
                .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse { SecretString = Convert.ToBase64String(new byte[32]) });

            SharedFunctions.Initialize(new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(new AWSKMSKeyProvider(secretsManager.Object, configuration))
                .BuildServiceProvider());

            return configuration;
        }
    }
}
