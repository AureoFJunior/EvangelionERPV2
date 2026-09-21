using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.NFeModule.Infra.Certificates;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    /// <summary>
    /// Exercises the real crypto path against a PKCS#12 generated in-test, so an
    /// X509CertificateLoader or key-storage-flag regression fails here rather than at SEFAZ.
    /// </summary>
    public class EnterpriseCertificateProviderTests
    {
        private const string Prefix = "evangelion/test/nfe/cert/";
        private const string Password = "pfx-password";
        private const string Cnpj = "11222333000181";

        private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

        #region Inspect

        [Fact]
        public void Inspect_ValidPfx_ReturnsThumbprintSubjectAndExpiry()
        {
            var pfx = CreatePfx($"NERV INDUSTRIA LTDA:{Cnpj}", notAfter: Now.AddDays(365));
            var (sut, _) = BuildSut();

            var metadata = sut.Inspect(pfx, Password);

            Assert.False(string.IsNullOrWhiteSpace(metadata.Thumbprint));
            Assert.Contains("NERV INDUSTRIA LTDA", metadata.SubjectCn);
            Assert.Equal(Cnpj, metadata.Cnpj);
            Assert.Equal(Now, metadata.UploadedAtUtc);
            Assert.True(metadata.NotAfterUtc > Now);
        }

        [Fact]
        public void Inspect_SubjectWithoutCnpj_ReturnsEmptyCnpj()
        {
            var pfx = CreatePfx("NERV INDUSTRIA LTDA", notAfter: Now.AddDays(365));
            var (sut, _) = BuildSut();

            Assert.Equal(string.Empty, sut.Inspect(pfx, Password).Cnpj);
        }

        [Fact]
        public void Inspect_WrongPassword_ThrowsInvalidUpload()
        {
            var pfx = CreatePfx($"NERV:{Cnpj}", notAfter: Now.AddDays(365));
            var (sut, _) = BuildSut();

            var ex = Assert.Throws<NFeCertificateException>(() => sut.Inspect(pfx, "wrong-password"));

            Assert.Equal(NFeCertificateException.InvalidUpload().Message, ex.Message);
        }

        [Fact]
        public void Inspect_NotAPkcs12_ThrowsInvalidUpload()
        {
            var (sut, _) = BuildSut();

            var ex = Assert.Throws<NFeCertificateException>(
                () => sut.Inspect("this is not a certificate"u8.ToArray(), Password));

            Assert.Equal(NFeCertificateException.InvalidUpload().Message, ex.Message);
        }

        [Fact]
        public void Inspect_EmptyPayload_ThrowsInvalidUpload()
        {
            var (sut, _) = BuildSut();

            Assert.Throws<NFeCertificateException>(() => sut.Inspect([], Password));
        }

        [Fact]
        public void Inspect_AlreadyExpiredCertificate_Throws()
        {
            var pfx = CreatePfx($"NERV:{Cnpj}", notAfter: Now.AddDays(-1));
            var (sut, _) = BuildSut();

            var ex = Assert.Throws<NFeCertificateException>(() => sut.Inspect(pfx, Password));

            Assert.Equal(NFeCertificateException.AlreadyExpiredUpload().Message, ex.Message);
        }

        #endregion

        #region GetAsync

        [Fact]
        public async Task GetAsync_WhenNoSecretStored_ThrowsMissing()
        {
            var (sut, _) = BuildSut();

            var ex = await Assert.ThrowsAsync<NFeCertificateException>(
                () => sut.GetAsync(Guid.NewGuid()));

            Assert.Equal(NFeCertificateException.Missing().Message, ex.Message);
        }

        [Fact]
        public async Task GetAsync_WithStoredCertificate_ReturnsUsablePrivateKey()
        {
            var enterpriseId = Guid.NewGuid();
            var pfx = CreatePfx($"NERV:{Cnpj}", notAfter: Now.AddDays(365));
            var (sut, secrets) = BuildSut();
            secrets[Prefix + enterpriseId.ToString("D")] = BuildSecretPayload(pfx, Password);

            var certificate = await sut.GetAsync(enterpriseId);

            Assert.True(certificate.HasPrivateKey);

            using var rsa = certificate.GetRSAPrivateKey();
            Assert.NotNull(rsa);

            var payload = "chave-de-acesso"u8.ToArray();
            var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            using var publicKey = certificate.GetRSAPublicKey();
            Assert.NotNull(publicKey);
            Assert.True(publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }

        [Fact]
        public async Task GetAsync_ExpiredStoredCertificate_ThrowsExpired()
        {
            var enterpriseId = Guid.NewGuid();
            var pfx = CreatePfx($"NERV:{Cnpj}", notAfter: Now.AddDays(10));
            var (sut, secrets) = BuildSut(now: Now.AddDays(20));
            secrets[Prefix + enterpriseId.ToString("D")] = BuildSecretPayload(pfx, Password);

            var ex = await Assert.ThrowsAsync<NFeCertificateException>(() => sut.GetAsync(enterpriseId));

            Assert.Equal(NFeCertificateException.Expired().Message, ex.Message);
        }

        [Fact]
        public async Task GetAsync_CorruptStoredMaterial_ThrowsUnreadable()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, secrets) = BuildSut();
            secrets[Prefix + enterpriseId.ToString("D")] =
                $"{{\"pfxBase64\":\"{Convert.ToBase64String("garbage"u8.ToArray())}\",\"password\":\"x\"}}";

            var ex = await Assert.ThrowsAsync<NFeCertificateException>(() => sut.GetAsync(enterpriseId));

            Assert.Equal(NFeCertificateException.Unreadable().Message, ex.Message);
        }

        [Fact]
        public async Task GetAsync_WithPlainSecretPrefix_ThrowsInEveryEnvironment()
        {
            var (sut, _) = BuildSut(prefix: "plain:");

            var ex = await Assert.ThrowsAsync<NFeCertificateException>(() => sut.GetAsync(Guid.NewGuid()));

            Assert.Equal(NFeCertificateException.PlainSecretsForbidden().Message, ex.Message);
        }

        [Fact]
        public async Task GetAsync_SecondCall_ServesFromCacheWithoutReadingSecretsAgain()
        {
            var enterpriseId = Guid.NewGuid();
            var pfx = CreatePfx($"NERV:{Cnpj}", notAfter: Now.AddDays(365));
            var (sut, secrets, secretsManager) = BuildSutWithMock();
            secrets[Prefix + enterpriseId.ToString("D")] = BuildSecretPayload(pfx, Password);

            await sut.GetAsync(enterpriseId);
            var callsAfterFirst = secretsManager.Invocations.Count;
            await sut.GetAsync(enterpriseId);

            Assert.Equal(callsAfterFirst, secretsManager.Invocations.Count);
        }

        [Fact]
        public async Task GetAsync_AfterInvalidate_ReadsTheRotatedCertificate()
        {
            var enterpriseId = Guid.NewGuid();
            var secretId = Prefix + enterpriseId.ToString("D");

            var original = CreatePfx($"NERV:{Cnpj}", notAfter: Now.AddDays(365));
            var (sut, secrets) = BuildSut();
            secrets[secretId] = BuildSecretPayload(original, Password);

            var before = await sut.GetAsync(enterpriseId);

            var rotated = CreatePfx($"NERV:{Cnpj}", notAfter: Now.AddDays(730));
            secrets[secretId] = BuildSecretPayload(rotated, Password);
            sut.Invalidate(enterpriseId);

            var after = await sut.GetAsync(enterpriseId);

            Assert.NotEqual(before.Thumbprint, after.Thumbprint);
        }

        #endregion

        #region StoreAsync / DeleteAsync

        [Fact]
        public async Task StoreAsync_WritesPfxAndPasswordUnderTheTenantSecret()
        {
            var enterpriseId = Guid.NewGuid();
            var pfx = CreatePfx($"NERV:{Cnpj}", notAfter: Now.AddDays(365));
            var (sut, secrets) = BuildSut();

            var metadata = await sut.StoreAsync(enterpriseId, pfx, Password);

            var stored = secrets[Prefix + enterpriseId.ToString("D")];
            Assert.Contains(Convert.ToBase64String(pfx), stored);
            Assert.Contains(Password, stored);
            Assert.Equal(Cnpj, metadata.Cnpj);
        }

        [Fact]
        public async Task StoreAsync_InvalidCertificate_WritesNothing()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, secrets) = BuildSut();

            await Assert.ThrowsAsync<NFeCertificateException>(
                () => sut.StoreAsync(enterpriseId, "not a pfx"u8.ToArray(), Password));

            Assert.Empty(secrets);
        }

        [Fact]
        public async Task StoreAsync_ThenGetAsync_RoundTrips()
        {
            var enterpriseId = Guid.NewGuid();
            var pfx = CreatePfx($"NERV:{Cnpj}", notAfter: Now.AddDays(365));
            var (sut, _) = BuildSut();

            var metadata = await sut.StoreAsync(enterpriseId, pfx, Password);
            var certificate = await sut.GetAsync(enterpriseId);

            Assert.Equal(metadata.Thumbprint, certificate.Thumbprint);
        }

        [Fact]
        public async Task DeleteAsync_RemovesTheSecret()
        {
            var enterpriseId = Guid.NewGuid();
            var pfx = CreatePfx($"NERV:{Cnpj}", notAfter: Now.AddDays(365));
            var (sut, secrets) = BuildSut();
            await sut.StoreAsync(enterpriseId, pfx, Password);

            await sut.DeleteAsync(enterpriseId);

            Assert.Empty(secrets);
            await Assert.ThrowsAsync<NFeCertificateException>(() => sut.GetAsync(enterpriseId));
        }

        [Fact]
        public async Task DeleteAsync_WhenNothingStored_IsIdempotent()
        {
            var (sut, _) = BuildSut();

            await sut.DeleteAsync(Guid.NewGuid());
        }

        #endregion

        #region Metadata helpers

        [Theory]
        [InlineData("NERV INDUSTRIA LTDA:11222333000181", "11222333000181")]
        [InlineData("11222333000181", "11222333000181")]
        [InlineData("NERV INDUSTRIA LTDA", "")]
        [InlineData("", "")]
        [InlineData(null, "")]
        [InlineData("NERV:1122233300018", "")]
        [InlineData("NERV:112223330001812", "")]
        public void ExtractCnpj_ParsesOnlyFourteenDigitRuns(string? subject, string expected)
        {
            Assert.Equal(expected, NFeCertificateMetadata.ExtractCnpj(subject));
        }

        [Theory]
        [InlineData(30, 30)]
        [InlineData(1, 1)]
        [InlineData(0, 0)]
        [InlineData(-5, -5)]
        public void DaysUntilExpiry_FloorsAndGoesNegativeOnceExpired(int offsetDays, int expected)
        {
            Assert.Equal(expected, NFeCertificateMetadata.DaysUntilExpiry(Now.AddDays(offsetDays), Now));
        }

        #endregion

        #region Helpers

        /// <summary>Generates a real RSA PKCS#12, so the crypto path is not stubbed out.</summary>
        private static byte[] CreatePfx(string commonName, DateTime notAfter)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                $"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            using var certificate = request.CreateSelfSigned(
                new DateTimeOffset(notAfter.AddYears(-1), TimeSpan.Zero),
                new DateTimeOffset(notAfter, TimeSpan.Zero));

            return certificate.Export(X509ContentType.Pkcs12, Password);
        }

        private static string BuildSecretPayload(byte[] pfx, string password) =>
            $"{{\"pfxBase64\":\"{Convert.ToBase64String(pfx)}\",\"password\":\"{password}\"}}";

        private static (EnterpriseCertificateProvider sut, Dictionary<string, string> secrets) BuildSut(
            string prefix = Prefix, DateTime? now = null)
        {
            var (sut, secrets, _) = BuildSutWithMock(prefix, now);
            return (sut, secrets);
        }

        private static (
            EnterpriseCertificateProvider sut,
            Dictionary<string, string> secrets,
            Mock<IAmazonSecretsManager> secretsManager)
            BuildSutWithMock(string prefix = Prefix, DateTime? now = null)
        {
            var secrets = new Dictionary<string, string>(StringComparer.Ordinal);
            var secretsManager = new Mock<IAmazonSecretsManager>();

            secretsManager
                .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((GetSecretValueRequest request, CancellationToken _) =>
                    secrets.TryGetValue(request.SecretId, out var value)
                        ? new GetSecretValueResponse { SecretString = value }
                        : throw new ResourceNotFoundException("missing"));

            secretsManager
                .Setup(x => x.PutSecretValueAsync(It.IsAny<PutSecretValueRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((PutSecretValueRequest request, CancellationToken _) =>
                {
                    if (!secrets.ContainsKey(request.SecretId))
                        throw new ResourceNotFoundException("missing");

                    secrets[request.SecretId] = request.SecretString;
                    return new PutSecretValueResponse();
                });

            secretsManager
                .Setup(x => x.CreateSecretAsync(It.IsAny<CreateSecretRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateSecretRequest request, CancellationToken _) =>
                {
                    secrets[request.Name] = request.SecretString;
                    return new CreateSecretResponse();
                });

            secretsManager
                .Setup(x => x.DeleteSecretAsync(It.IsAny<DeleteSecretRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DeleteSecretRequest request, CancellationToken _) =>
                {
                    if (!secrets.Remove(request.SecretId))
                        throw new ResourceNotFoundException("missing");

                    return new DeleteSecretResponse();
                });

            var configuration = new ConfigurationBuilder().Build();
            var kmsProvider = new AWSKMSKeyProvider(secretsManager.Object, configuration);

            var sut = new EnterpriseCertificateProvider(
                secretsManager.Object,
                kmsProvider,
                new MemoryCache(new MemoryCacheOptions()),
                prefix,
                new FixedTimeProvider(now ?? Now));

            return (sut, secrets, secretsManager);
        }

        private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
        }

        #endregion
    }
}
