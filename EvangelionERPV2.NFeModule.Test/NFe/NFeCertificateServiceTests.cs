using System.Linq.Expressions;
using System.Security.Cryptography.X509Certificates;
using EvangelionERPV2.NFeModule.Application.Services;
using EvangelionERPV2.NFeModule.Infra.Certificates;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    public class NFeCertificateServiceTests
    {
        private const string Cnpj = "11222333000181";
        private const string OtherCnpj = "11444777000161";

        private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);
        private static readonly byte[] Pfx = [1, 2, 3, 4];

        #region Upload

        [Fact]
        public async Task UploadAsync_WithoutFiscalProfile_ShouldThrow()
        {
            // Without a configured CNPJ there is nothing to match the certificate against.
            var (sut, _, _) = BuildSut();

            var ex = await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UploadAsync(Guid.NewGuid(), Pfx, "pwd"));

            Assert.Contains("fiscal profile", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task UploadAsync_WhenCertificateCnpjDiffersFromProfile_ShouldThrowAndStoreNothing()
        {
            // SEFAZ rejects every note in this state; catching it here beats discovering it at
            // issuance, after a number has been burned.
            var enterpriseId = Guid.NewGuid();
            var (sut, _, provider) = BuildSut(
                settings: [BuildSettings(enterpriseId)],
                metadata: BuildMetadata(OtherCnpj));

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UploadAsync(enterpriseId, Pfx, "pwd"));

            provider.Verify(x => x.StoreAsync(
                It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task UploadAsync_WhenCertificateHasNoCnpj_ShouldNotBlockTheUpload()
        {
            // A subject without a parseable CNPJ cannot be checked; refusing it would lock out
            // legitimate certificates whose CN is formatted differently.
            var enterpriseId = Guid.NewGuid();
            var (sut, _, provider) = BuildSut(
                settings: [BuildSettings(enterpriseId)],
                metadata: BuildMetadata(cnpj: string.Empty));

            var status = await sut.UploadAsync(enterpriseId, Pfx, "pwd");

            Assert.True(status.IsConfigured);
            provider.Verify(x => x.StoreAsync(
                enterpriseId, Pfx, "pwd", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UploadAsync_ValidCertificate_ShouldPersistMetadataOnTheProfile()
        {
            var enterpriseId = Guid.NewGuid();
            var settings = BuildSettings(enterpriseId);
            var (sut, stores, _) = BuildSut(settings: [settings], metadata: BuildMetadata(Cnpj));

            var status = await sut.UploadAsync(enterpriseId, Pfx, "pwd");

            Assert.Equal("THUMB", settings.CertThumbprint);
            Assert.Equal($"NERV:{Cnpj}", settings.CertSubjectCn);
            Assert.NotNull(settings.CertNotAfterUtc);
            Assert.NotNull(settings.CertUploadedAtUtc);
            Assert.Equal(Now, settings.UpdatedAt);

            Assert.True(status.IsConfigured);
            Assert.True(status.CnpjMatchesFiscalProfile);
            Assert.False(status.IsExpired);

            stores.settingsRepo.Verify(x => x.Update(settings), Times.Once);
            stores.settingsRepo.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UploadAsync_ShouldNeverPersistKeyMaterial()
        {
            // The entity has no column that could hold a pfx or a password; this asserts the
            // shape stays that way.
            var enterpriseId = Guid.NewGuid();
            var settings = BuildSettings(enterpriseId);
            var (sut, _, _) = BuildSut(settings: [settings], metadata: BuildMetadata(Cnpj));

            await sut.UploadAsync(enterpriseId, Pfx, "super-secret-password");

            var persisted = typeof(EnterpriseFiscalSettings)
                .GetProperties()
                .Where(property => property.PropertyType == typeof(string))
                .Select(property => property.GetValue(settings) as string)
                .Where(value => !string.IsNullOrEmpty(value));

            Assert.DoesNotContain("super-secret-password", persisted);
            Assert.DoesNotContain(Convert.ToBase64String(Pfx), persisted);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(NFeCertificateService.MaxCertificateBytes + 1)]
        public async Task UploadAsync_WithBadPayloadSize_ShouldThrow(int size)
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _, _) = BuildSut(settings: [BuildSettings(enterpriseId)]);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UploadAsync(enterpriseId, new byte[size], "pwd"));
        }

        [Fact]
        public async Task UploadAsync_WithEmptyEnterprise_ShouldThrow()
        {
            var (sut, _, _) = BuildSut();

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UploadAsync(Guid.Empty, Pfx, "pwd"));
        }

        #endregion

        #region Status

        [Fact]
        public async Task GetStatusAsync_WithoutProfile_ShouldReportNotConfigured()
        {
            var (sut, _, _) = BuildSut();

            var status = await sut.GetStatusAsync(Guid.NewGuid());

            Assert.False(status.IsConfigured);
            Assert.Null(status.Thumbprint);
        }

        [Fact]
        public async Task GetStatusAsync_WithProfileButNoCertificate_ShouldReportNotConfigured()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _, _) = BuildSut(settings: [BuildSettings(enterpriseId)]);

            Assert.False((await sut.GetStatusAsync(enterpriseId)).IsConfigured);
        }

        [Theory]
        [InlineData(365, false, false)]
        [InlineData(31, false, false)]
        [InlineData(30, false, true)]
        [InlineData(1, false, true)]
        [InlineData(-1, true, false)]
        public async Task GetStatusAsync_ShouldFlagExpiryWindows(int daysFromNow, bool expired, bool expiringSoon)
        {
            var enterpriseId = Guid.NewGuid();
            var settings = BuildSettings(enterpriseId);
            settings.CertThumbprint = "THUMB";
            settings.CertSubjectCn = $"NERV:{Cnpj}";
            settings.CertNotAfterUtc = Now.AddDays(daysFromNow);

            var (sut, _, _) = BuildSut(settings: [settings]);

            var status = await sut.GetStatusAsync(enterpriseId);

            Assert.True(status.IsConfigured);
            Assert.Equal(expired, status.IsExpired);
            Assert.Equal(expiringSoon, status.IsExpiringSoon);
            Assert.Equal(daysFromNow, status.DaysUntilExpiry);
        }

        [Fact]
        public async Task GetStatusAsync_WhenStoredCertificateCnpjDiverged_ShouldReportMismatch()
        {
            // Reachable by editing the profile's CNPJ after uploading a certificate.
            var enterpriseId = Guid.NewGuid();
            var settings = BuildSettings(enterpriseId);
            settings.CertThumbprint = "THUMB";
            settings.CertSubjectCn = $"NERV:{OtherCnpj}";
            settings.CertNotAfterUtc = Now.AddDays(200);

            var (sut, _, _) = BuildSut(settings: [settings]);

            var status = await sut.GetStatusAsync(enterpriseId);

            Assert.False(status.CnpjMatchesFiscalProfile);

            // The mismatch is reported without disclosing either CNPJ: this response is readable at
            // Manager, while emitente identity stays behind the admin-only settings endpoint.
            Assert.DoesNotContain(
                OtherCnpj,
                System.Text.Json.JsonSerializer.Serialize(status),
                StringComparison.Ordinal);
        }

        #endregion

        #region Delete

        [Fact]
        public async Task DeleteAsync_ShouldClearMetadataAndDisableIssuance()
        {
            // Without a signing identity every note would fail at transmission; disabling here
            // makes the reason visible in the settings screen instead.
            var enterpriseId = Guid.NewGuid();
            var settings = BuildSettings(enterpriseId);
            settings.CertThumbprint = "THUMB";
            settings.CertSubjectCn = $"NERV:{Cnpj}";
            settings.CertNotAfterUtc = Now.AddDays(200);
            settings.CertUploadedAtUtc = Now;
            settings.Enabled = true;

            var (sut, _, provider) = BuildSut(settings: [settings]);

            await sut.DeleteAsync(enterpriseId);

            Assert.Null(settings.CertThumbprint);
            Assert.Null(settings.CertSubjectCn);
            Assert.Null(settings.CertNotAfterUtc);
            Assert.Null(settings.CertUploadedAtUtc);
            Assert.False(settings.Enabled);

            provider.Verify(x => x.DeleteAsync(enterpriseId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteAsync_WithoutProfile_ShouldStillClearTheSecret()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _, provider) = BuildSut();

            await sut.DeleteAsync(enterpriseId);

            provider.Verify(x => x.DeleteAsync(enterpriseId, It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region Helpers

        private static EnterpriseFiscalSettings BuildSettings(Guid enterpriseId) =>
            new() { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, Cnpj = Cnpj, IsActive = true };

        private static NFeCertificateMetadata BuildMetadata(string cnpj) =>
            new("THUMB", $"NERV:{cnpj}", cnpj, Now.AddDays(365), Now);

        private static (
            NFeCertificateService sut,
            (Mock<IRepository<EnterpriseFiscalSettings>> settingsRepo, List<EnterpriseFiscalSettings> settings) stores,
            Mock<IEnterpriseCertificateProvider> provider)
            BuildSut(
                List<EnterpriseFiscalSettings>? settings = null,
                NFeCertificateMetadata? metadata = null)
        {
            var store = settings ?? [];
            var resolvedMetadata = metadata ?? BuildMetadata(Cnpj);

            var provider = new Mock<IEnterpriseCertificateProvider>();
            provider.Setup(x => x.Inspect(It.IsAny<byte[]>(), It.IsAny<string>()))
                .Returns(resolvedMetadata);
            provider.Setup(x => x.StoreAsync(
                    It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resolvedMetadata);
            provider.Setup(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            provider.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((X509Certificate2)null!);

            var settingsRepo = new Mock<IRepository<EnterpriseFiscalSettings>>();
            settingsRepo
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<EnterpriseFiscalSettings, bool>>>()))
                .ReturnsAsync((Expression<Func<EnterpriseFiscalSettings, bool>>? predicate) =>
                    predicate == null ? store.ToList() : store.Where(predicate.Compile()).ToList());
            settingsRepo.Setup(x => x.Update(It.IsAny<EnterpriseFiscalSettings>()))
                .Returns((EnterpriseFiscalSettings entity) => entity);
            settingsRepo.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var sut = new NFeCertificateService(
                provider.Object, settingsRepo.Object, new FixedTimeProvider(Now));

            return (sut, (settingsRepo, store), provider);
        }

        private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
        }

        #endregion
    }
}
