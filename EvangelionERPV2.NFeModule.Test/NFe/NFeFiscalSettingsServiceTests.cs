using System.Linq.Expressions;
using EvangelionERPV2.NFeModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    public class NFeFiscalSettingsServiceTests
    {
        private const string ValidCnpj = "11222333000181";
        private const string ValidCnpjMasked = "11.222.333/0001-81";

        #region GetSettingsAsync

        [Fact]
        public async Task GetSettingsAsync_WhenEnterpriseIsEmpty_ShouldThrow()
        {
            var (sut, _) = BuildSut();

            await Assert.ThrowsAsync<InsertDatabaseException>(() => sut.GetSettingsAsync(Guid.Empty));
        }

        [Fact]
        public async Task GetSettingsAsync_WhenTenantHasNoRow_ShouldReturnTransientDefaults()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, stores) = BuildSut();

            var result = await sut.GetSettingsAsync(enterpriseId);

            Assert.Equal(enterpriseId, result.EnterpriseId);
            Assert.Equal(2, result.Environment);
            Assert.False(result.Enabled);
            Assert.Equal("5102", result.DefaultCfopIntra);
            Assert.Empty(stores.settings);
        }

        [Fact]
        public async Task GetSettingsAsync_WhenNoRowExists_ShouldReturnDefaults()
        {
            var (sut, _) = BuildSut();

            var result = await sut.GetSettingsAsync(Guid.NewGuid());

            Assert.NotNull(result);
            Assert.False(result.Enabled);
        }

        [Fact]
        public async Task GetSettingsAsync_WhenRowExists_ShouldReturnStoredRow()
        {
            var enterpriseId = Guid.NewGuid();
            var stored = BuildValidSettings(enterpriseId);
            stored.RazaoSocial = "NERV LTDA";

            var (sut, _) = BuildSut(settings: [stored]);

            var result = await sut.GetSettingsAsync(enterpriseId);

            Assert.Equal("NERV LTDA", result.RazaoSocial);
        }

        [Fact]
        public async Task GetSettingsAsync_ShouldIgnoreOtherTenantsRows()
        {
            var stored = BuildValidSettings(Guid.NewGuid());
            var (sut, _) = BuildSut(settings: [stored]);

            var result = await sut.GetSettingsAsync(Guid.NewGuid());

            Assert.Empty(result.Cnpj);
        }

        #endregion

        #region UpsertSettingsAsync — happy path

        [Fact]
        public async Task UpsertSettingsAsync_WhenNoRowExists_ShouldCreateAndStampAudit()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, stores) = BuildSut();

            var result = await sut.UpsertSettingsAsync(enterpriseId, BuildValidSettings(enterpriseId));

            Assert.Equal(enterpriseId, result.EnterpriseId);
            Assert.True(result.IsActive);
            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.NotNull(result.UpdatedAt);
            stores.settingsRepo.Verify(x => x.CreateAsync(It.IsAny<EnterpriseFiscalSettings>()), Times.Once);
            stores.settingsRepo.Verify(x => x.Update(It.IsAny<EnterpriseFiscalSettings>()), Times.Never);
        }

        [Fact]
        public async Task UpsertSettingsAsync_WhenRowExists_ShouldUpdateInPlace()
        {
            var enterpriseId = Guid.NewGuid();
            var stored = BuildValidSettings(enterpriseId);
            var (sut, stores) = BuildSut(settings: [stored]);

            var incoming = BuildValidSettings(enterpriseId);
            incoming.RazaoSocial = "SEELE SA";

            var result = await sut.UpsertSettingsAsync(enterpriseId, incoming);

            Assert.Same(stored, result);
            Assert.Equal("SEELE SA", result.RazaoSocial);
            stores.settingsRepo.Verify(x => x.CreateAsync(It.IsAny<EnterpriseFiscalSettings>()), Times.Never);
            stores.settingsRepo.Verify(x => x.Update(It.IsAny<EnterpriseFiscalSettings>()), Times.Once);
        }

        [Fact]
        public async Task UpsertSettingsAsync_ShouldNotLetThePayloadOverwriteCertificateMetadata()
        {
            var enterpriseId = Guid.NewGuid();
            var stored = BuildValidSettings(enterpriseId);
            stored.CertThumbprint = "REAL-THUMBPRINT";
            stored.CertSubjectCn = "NERV";

            var (sut, _) = BuildSut(settings: [stored]);

            var incoming = BuildValidSettings(enterpriseId);
            incoming.CertThumbprint = "ATTACKER-SUPPLIED";
            incoming.CertSubjectCn = "SEELE";

            var result = await sut.UpsertSettingsAsync(enterpriseId, incoming);

            Assert.Equal("REAL-THUMBPRINT", result.CertThumbprint);
            Assert.Equal("NERV", result.CertSubjectCn);
        }

        #endregion

        #region Normalization

        [Theory]
        [InlineData("SP", (short)35)]
        [InlineData("RS", (short)43)]
        [InlineData("SC", (short)42)]
        [InlineData("PR", (short)41)]
        public async Task UpsertSettingsAsync_ShouldDeriveIbgeUfCodeFromUf(string uf, short expected)
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId, uf: uf);
            settings.CodigoUfIbge = 99;

            var result = await sut.UpsertSettingsAsync(enterpriseId, settings);

            Assert.Equal(expected, result.CodigoUfIbge);
        }

        [Fact]
        public async Task UpsertSettingsAsync_ShouldAcceptLowerCaseUf()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var result = await sut.UpsertSettingsAsync(enterpriseId, BuildValidSettings(enterpriseId, uf: "sp"));

            Assert.Equal("SP", result.Uf);
            Assert.Equal((short)35, result.CodigoUfIbge);
        }

        [Fact]
        public async Task UpsertSettingsAsync_ShouldStripMasksFromDocumentsAndCep()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.Cnpj = ValidCnpjMasked;
            settings.Cep = "01310-100";
            settings.Telefone = "(11) 99999-8888";

            var result = await sut.UpsertSettingsAsync(enterpriseId, settings);

            Assert.Equal(ValidCnpj, result.Cnpj);
            Assert.Equal("01310100", result.Cep);
            Assert.Equal("11999998888", result.Telefone);
        }

        [Fact]
        public async Task UpsertSettingsAsync_ShouldFallBackToDefaultsForBlankOptionalFields()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.NFeSeries = "   ";
            settings.SchemaVersion = "";
            settings.CodigoPais = "";
            settings.DefaultUnidadeTributavel = " ";
            settings.NomeFantasia = "   ";

            var result = await sut.UpsertSettingsAsync(enterpriseId, settings);

            Assert.Equal("1", result.NFeSeries);
            Assert.Equal("4.00", result.SchemaVersion);
            Assert.Equal("1058", result.CodigoPais);
            Assert.Equal("UN", result.DefaultUnidadeTributavel);
            Assert.Null(result.NomeFantasia);
        }

        #endregion

        #region Validation

        [Fact]
        public async Task UpsertSettingsAsync_WhenSettingsIsNull_ShouldThrow()
        {
            var (sut, _) = BuildSut();

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(Guid.NewGuid(), null!));
        }

        [Fact]
        public async Task UpsertSettingsAsync_WhenEnterpriseIsEmpty_ShouldThrow()
        {
            var (sut, _) = BuildSut();

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(Guid.Empty, BuildValidSettings(Guid.NewGuid())));
        }

        [Theory]
        [InlineData("")]
        [InlineData("11222333000180")]
        [InlineData("11111111111111")]
        [InlineData("1122233300018")]
        [InlineData("52998224725")]
        public async Task UpsertSettingsAsync_WithInvalidCnpj_ShouldThrow(string cnpj)
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.Cnpj = cnpj;

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));
        }

        [Theory]
        [InlineData("MG")]
        [InlineData("XX")]
        [InlineData("")]
        public async Task UpsertSettingsAsync_WithUnsupportedUf_ShouldThrow(string uf)
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.Uf = uf;

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));
        }

        [Fact]
        public async Task UpsertSettingsAsync_WhenIbgeCodeBelongsToAnotherUf_ShouldThrow()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId, uf: "SP");
            settings.CodigoMunicipioIbge = "4106902";

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));
        }

        [Fact]
        public async Task UpsertSettingsAsync_WithMatchingIbgeCode_ShouldSucceed()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId, uf: "PR");
            settings.CodigoMunicipioIbge = "4106902";

            var result = await sut.UpsertSettingsAsync(enterpriseId, settings);

            Assert.Equal("4106902", result.CodigoMunicipioIbge);
        }

        [Theory]
        [InlineData("123456")]
        [InlineData("123456789")]
        public async Task UpsertSettingsAsync_WithWrongLengthIbgeCode_ShouldThrow(string code)
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.CodigoMunicipioIbge = code;

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));
        }

        [Fact]
        public async Task UpsertSettingsAsync_WhenRegimeNormalHasNoCst_ShouldThrow()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.Crt = 3;
            settings.DefaultCst = null;

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));
        }

        [Theory]
        [InlineData((byte)1)]
        [InlineData((byte)2)]
        public async Task UpsertSettingsAsync_WhenSimplesNacionalHasNoCsosn_ShouldThrow(byte crt)
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.Crt = crt;
            settings.DefaultCst = null;
            settings.DefaultCsosn = null;

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));
        }

        [Fact]
        public async Task UpsertSettingsAsync_WhenSimplesNacionalHasCsosn_ShouldSucceed()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.Crt = 1;
            settings.DefaultCst = null;
            settings.DefaultCsosn = "102";

            var result = await sut.UpsertSettingsAsync(enterpriseId, settings);

            Assert.Equal("102", result.DefaultCsosn);
        }

        [Theory]
        [InlineData((byte)0)]
        [InlineData((byte)5)]
        public async Task UpsertSettingsAsync_WithInvalidCrt_ShouldThrow(byte crt)
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.Crt = crt;

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));
        }

        [Theory]
        [InlineData((byte)0)]
        [InlineData((byte)3)]
        public async Task UpsertSettingsAsync_WithInvalidEnvironment_ShouldThrow(byte environment)
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.Environment = environment;

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));
        }

        [Theory]
        [InlineData("510")]
        [InlineData("51022")]
        [InlineData("51A2")]
        [InlineData("")]
        public async Task UpsertSettingsAsync_WithInvalidCfop_ShouldThrow(string cfop)
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.DefaultCfopIntra = cfop;

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));
        }

        [Theory]
        [InlineData(-0.01)]
        [InlineData(1.01)]
        public async Task UpsertSettingsAsync_WithOutOfRangeIcmsRate_ShouldThrow(double rate)
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.DefaultAliquotaIcms = (decimal)rate;

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1441)]
        public async Task UpsertSettingsAsync_WithOutOfRangeCancellationWindow_ShouldThrow(int minutes)
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.NFeCancellationWindowMinutes = minutes;

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));
        }

        [Fact]
        public async Task UpsertSettingsAsync_ShouldKeepConfiguredCancellationWindows()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.NFeCancellationWindowMinutes = 1440;
            settings.NFCeCancellationWindowMinutes = 30;

            var result = await sut.UpsertSettingsAsync(enterpriseId, settings);

            Assert.Equal(1440, result.NFeCancellationWindowMinutes);
            Assert.Equal(30, result.NFCeCancellationWindowMinutes);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task UpsertSettingsAsync_WithoutRazaoSocial_ShouldThrow(string razaoSocial)
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.RazaoSocial = razaoSocial;

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));
        }

        [Fact]
        public async Task UpsertSettingsAsync_WithIncompleteAddress_ShouldThrow()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.Bairro = "";

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));
        }

        [Fact]
        public async Task UpsertSettingsAsync_WithInvalidCep_ShouldThrow()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.Cep = "0131010";

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));
        }

        [Fact]
        public async Task UpsertSettingsAsync_WhenValidationFails_ShouldNotWriteAnything()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, stores) = BuildSut();

            var settings = BuildValidSettings(enterpriseId);
            settings.Cnpj = "invalid";

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(enterpriseId, settings));

            stores.settingsRepo.Verify(x => x.CreateAsync(It.IsAny<EnterpriseFiscalSettings>()), Times.Never);
            stores.sequenceRepo.Verify(x => x.CreateAsync(It.IsAny<NFeNumberSequence>()), Times.Never);
        }

        #endregion

        #region Number sequences

        [Fact]
        public async Task UpsertSettingsAsync_ShouldCreateSequencesForBothModelsAndEnvironments()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, stores) = BuildSut();

            await sut.UpsertSettingsAsync(enterpriseId, BuildValidSettings(enterpriseId));

            Assert.Equal(4, stores.sequences.Count);
            Assert.All(stores.sequences, sequence =>
            {
                Assert.Equal(enterpriseId, sequence.EnterpriseId);
                Assert.Equal(1, sequence.NextNumber);
                Assert.True(sequence.IsActive);
            });

            Assert.Equal(
                [(55, 1), (55, 2), (65, 1), (65, 2)],
                stores.sequences
                    .Select(x => ((int)x.Model, (int)x.Environment))
                    .OrderBy(x => x.Item1).ThenBy(x => x.Item2)
                    .ToArray());
        }

        [Fact]
        public async Task UpsertSettingsAsync_WhenSequencesAlreadyExist_ShouldNotResetTheCounter()
        {
            var enterpriseId = Guid.NewGuid();
            var existing = new List<NFeNumberSequence>
            {
                BuildSequence(enterpriseId, 55, "1", 1, nextNumber: 4711),
                BuildSequence(enterpriseId, 55, "1", 2, nextNumber: 12),
                BuildSequence(enterpriseId, 65, "1", 1, nextNumber: 900),
                BuildSequence(enterpriseId, 65, "1", 2, nextNumber: 3)
            };

            var (sut, stores) = BuildSut(sequences: existing);

            await sut.UpsertSettingsAsync(enterpriseId, BuildValidSettings(enterpriseId));

            Assert.Equal(4, stores.sequences.Count);
            Assert.Equal(4711, stores.sequences.Single(x => x.Model == 55 && x.Environment == 1).NextNumber);
            Assert.Equal(900, stores.sequences.Single(x => x.Model == 65 && x.Environment == 1).NextNumber);
            stores.sequenceRepo.Verify(x => x.CreateAsync(It.IsAny<NFeNumberSequence>()), Times.Never);
        }

        [Fact]
        public async Task UpsertSettingsAsync_WhenSeriesChanges_ShouldCreateSequencesForTheNewSeries()
        {
            var enterpriseId = Guid.NewGuid();
            var existing = new List<NFeNumberSequence>
            {
                BuildSequence(enterpriseId, 55, "1", 1, nextNumber: 88),
                BuildSequence(enterpriseId, 55, "1", 2, nextNumber: 5),
                BuildSequence(enterpriseId, 65, "1", 1, nextNumber: 2),
                BuildSequence(enterpriseId, 65, "1", 2, nextNumber: 2)
            };

            var (sut, stores) = BuildSut(sequences: existing);

            var settings = BuildValidSettings(enterpriseId);
            settings.NFeSeries = "2";

            await sut.UpsertSettingsAsync(enterpriseId, settings);

            Assert.Equal(6, stores.sequences.Count);
            Assert.Equal(88, stores.sequences.Single(x => x.Model == 55 && x.Series == "1" && x.Environment == 1).NextNumber);
            Assert.Equal(2, stores.sequences.Count(x => x.Model == 55 && x.Series == "2"));
        }

        [Fact]
        public async Task UpsertSettingsAsync_ShouldNotAdoptAnotherTenantsSequence()
        {
            var enterpriseId = Guid.NewGuid();
            var foreign = BuildSequence(Guid.NewGuid(), 55, "1", 1, nextNumber: 5000);

            var (sut, stores) = BuildSut(sequences: [foreign]);

            await sut.UpsertSettingsAsync(enterpriseId, BuildValidSettings(enterpriseId));

            Assert.Equal(4, stores.sequences.Count(x => x.EnterpriseId == enterpriseId));
            Assert.Equal(5000, foreign.NextNumber);
        }

        #endregion

        #region Helpers

        private static EnterpriseFiscalSettings BuildValidSettings(Guid enterpriseId, string uf = "SP")
        {
            return new EnterpriseFiscalSettings
            {
                EnterpriseId = enterpriseId,
                Cnpj = ValidCnpj,
                InscricaoEstadual = "110042490114",
                RazaoSocial = "NERV INDUSTRIA LTDA",
                Crt = 3,
                DefaultCst = "00",
                Logradouro = "Avenida Paulista",
                Numero = "1000",
                Bairro = "Bela Vista",
                CodigoMunicipioIbge = uf switch
                {
                    "PR" => "4106902",
                    "SC" => "4205407",
                    "RS" => "4314902",
                    _ => "3550308"
                },
                Municipio = "Sao Paulo",
                Uf = uf,
                Cep = "01310100",
                Environment = 2
            };
        }

        private static NFeNumberSequence BuildSequence(
            Guid enterpriseId, short model, string series, byte environment, int nextNumber)
        {
            return new NFeNumberSequence
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Model = model,
                Series = series,
                Environment = environment,
                NextNumber = nextNumber,
                IsActive = true
            };
        }

        private static (
            NFeFiscalSettingsService sut,
            (Mock<IRepository<EnterpriseFiscalSettings>> settingsRepo,
             List<EnterpriseFiscalSettings> settings,
             Mock<IRepository<NFeNumberSequence>> sequenceRepo,
             List<NFeNumberSequence> sequences) stores)
            BuildSut(
                List<EnterpriseFiscalSettings>? settings = null,
                List<NFeNumberSequence>? sequences = null,
                bool throwsWhenEmpty = false)
        {
            var settingsStore = settings ?? [];
            var sequenceStore = sequences ?? [];

            var settingsRepo = CreateRepositoryMock(settingsStore, throwsWhenEmpty);
            var sequenceRepo = CreateRepositoryMock(sequenceStore, throwsWhenEmpty);

            var sut = new NFeFiscalSettingsService(settingsRepo.Object, sequenceRepo.Object);

            return (sut, (settingsRepo, settingsStore, sequenceRepo, sequenceStore));
        }

        private static Mock<IRepository<TEntity>> CreateRepositoryMock<TEntity>(
            List<TEntity> records, bool throwsWhenEmpty = false)
            where TEntity : BaseEntity
        {
            var repo = new Mock<IRepository<TEntity>>();

            repo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<TEntity, bool>>>()))
                .ReturnsAsync((Expression<Func<TEntity, bool>>? predicate) => predicate == null
                    ? records.ToList()
                    : records.Where(predicate.Compile()).ToList());

            repo.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task> operation, CancellationToken _) => operation());

            repo.Setup(x => x.CreateAsync(It.IsAny<TEntity>()))
                .ReturnsAsync((TEntity entity) =>
                {
                    records.Add(entity);
                    return entity;
                });

            repo.Setup(x => x.Update(It.IsAny<TEntity>()))
                .Returns((TEntity entity) => entity);

            repo.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            return repo;
        }

        #endregion
    }
}
