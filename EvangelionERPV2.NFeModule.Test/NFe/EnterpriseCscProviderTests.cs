using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.NFeModule.Infra.Certificates;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Reflection;
using Xunit;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    /// <summary>
    /// The CSC is a signing key, not a setting: it keys the HMAC behind every NFC-e consumer QR, so
    /// anyone holding it can mint QR codes that verify as the shop's.
    /// </summary>
    public class EnterpriseCscProviderTests
    {
        private static readonly Guid EnterpriseId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        [Fact]
        public async Task StoringACsc_WritesItToSecretsManagerAndNowhereElse()
        {
            var secrets = new Mock<IAmazonSecretsManager>();
            PutSecretValueRequest? written = null;

            secrets
                .Setup(x => x.PutSecretValueAsync(It.IsAny<PutSecretValueRequest>(), It.IsAny<CancellationToken>()))
                .Callback<PutSecretValueRequest, CancellationToken>((request, _) => written = request)
                .ReturnsAsync(new PutSecretValueResponse());

            await Provider(secrets).StoreAsync(EnterpriseId, "the-csc");

            Assert.NotNull(written);
            Assert.Equal($"evangelion/test/nfe/csc/{EnterpriseId}", written!.SecretId);
            Assert.Contains("the-csc", written.SecretString);
        }

        [Fact]
        public async Task TheFirstCscForATenant_CreatesTheSecret()
        {
            // A tenant that has never been given one has no secret at all. That is an expected
            // state, not a fault.
            var secrets = new Mock<IAmazonSecretsManager>();
            var created = false;

            secrets
                .Setup(x => x.PutSecretValueAsync(It.IsAny<PutSecretValueRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ResourceNotFoundException("no such secret"));
            secrets
                .Setup(x => x.CreateSecretAsync(It.IsAny<CreateSecretRequest>(), It.IsAny<CancellationToken>()))
                .Callback(() => created = true)
                .ReturnsAsync(new CreateSecretResponse());

            await Provider(secrets).StoreAsync(EnterpriseId, "the-csc");

            Assert.True(created);
        }

        [Fact]
        public async Task AnEmptyCsc_IsRefusedBeforeAnythingIsWritten()
        {
            var secrets = new Mock<IAmazonSecretsManager>(MockBehavior.Strict);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Provider(secrets).StoreAsync(EnterpriseId, "   "));
        }

        /// <summary>
        /// The `plain:` escape hatch is gated only by ASPNETCORE_ENVIRONMENT. Adequate for a
        /// connection string, not for a value that signs consumer documents.
        /// </summary>
        [Fact]
        public async Task APlainSecretPrefix_IsRefused()
        {
            var secrets = new Mock<IAmazonSecretsManager>(MockBehavior.Strict);
            var provider = Provider(secrets, prefix: "plain:");

            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.StoreAsync(EnterpriseId, "the-csc"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAsync(EnterpriseId));
        }

        /// <summary>
        /// The whole posture in one assertion: there is no way to ask for a CSC back through the
        /// API, because the request DTO is the only shape that carries one and nothing returns it.
        /// </summary>
        [Fact]
        public void NoResponseShapeCarriesACsc()
        {
            var carriers = typeof(UpdateNFCeCscRequestDTO).Assembly
                .GetTypes()
                .Where(type => type.Name.EndsWith("DTO", StringComparison.Ordinal))
                .Where(type => type != typeof(UpdateNFCeCscRequestDTO))
                .Where(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Any(property => property.Name.Equals("Csc", StringComparison.OrdinalIgnoreCase)))
                .Select(type => type.Name)
                .ToList();

            Assert.Empty(carriers);
        }

        private static EnterpriseCscProvider Provider(Mock<IAmazonSecretsManager> secrets, string? prefix = null) =>
            new(secrets.Object,
                new AWSKMSKeyProvider(secrets.Object, new ConfigurationBuilder().Build()),
                new MemoryCache(new MemoryCacheOptions()),
                prefix ?? "evangelion/test/nfe/csc/");
    }
}
