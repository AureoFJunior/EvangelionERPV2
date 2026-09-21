using EvangelionERPV2.EmployeeModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Moq;
using System.Reflection;

namespace EvangelionERPV2.EmployeeModule.Test
{
    public class EmployeeAbsenceServiceTests
    {
        private const string PlainCid = "M54.5";

        [Fact]
        public async Task CreateAsync_WithACid_ShouldStoreItEncrypted()
        {
            EnsureEncryptionKeyInitialized();
            var (sut, store) = BuildSut();

            var created = await sut.CreateAsync(BuildAbsence(EnumEmployeeAbsenceType.Atestado), PlainCid);

            var stored = Assert.Single(store);
            Assert.Same(created, stored);
            Assert.NotNull(stored.Cid);
            Assert.NotEqual(PlainCid, stored.Cid);
            Assert.Equal(PlainCid, SharedFunctions.Decrypt(stored.Cid!));
        }

        [Fact]
        public async Task CreateAsync_WithoutACid_ShouldStoreNull()
        {
            var (sut, store) = BuildSut();

            await sut.CreateAsync(BuildAbsence(EnumEmployeeAbsenceType.Falta));

            Assert.Null(Assert.Single(store).Cid);
        }

        [Fact]
        public async Task CreateAsync_WithACidOnSomethingOtherThanAnAtestado_ShouldThrow()
        {
            EnsureEncryptionKeyInitialized();
            var (sut, store) = BuildSut();

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.CreateAsync(BuildAbsence(EnumEmployeeAbsenceType.Falta), PlainCid));

            Assert.Empty(store);
        }

        [Fact]
        public async Task CreateAsync_WhenEncryptionIsUnavailable_ShouldRefuseToStoreThePlainCid()
        {
            var encryptionField = EncryptionKeyField();
            var originalValue = encryptionField?.GetValue(null);
            try
            {
                encryptionField?.SetValue(null, string.Empty);
                var (sut, store) = BuildSut();

                await Assert.ThrowsAsync<InsertDatabaseException>(
                    () => sut.CreateAsync(BuildAbsence(EnumEmployeeAbsenceType.Atestado), PlainCid));

                Assert.Empty(store);
            }
            finally
            {
                encryptionField?.SetValue(null, originalValue);
            }
        }

        [Fact]
        public async Task CreateAsync_WithAnEndDateBeforeTheStartDate_ShouldThrow()
        {
            var (sut, store) = BuildSut();
            var absence = BuildAbsence(EnumEmployeeAbsenceType.Falta);
            absence.EndDate = absence.StartDate.AddDays(-1);

            await Assert.ThrowsAsync<InsertDatabaseException>(() => sut.CreateAsync(absence));

            Assert.Empty(store);
        }

        [Fact]
        public async Task CreateAsync_WithAnUnknownType_ShouldThrow()
        {
            var (sut, _) = BuildSut();
            var absence = BuildAbsence(EnumEmployeeAbsenceType.Falta);
            absence.Type = 99;

            await Assert.ThrowsAsync<InsertDatabaseException>(() => sut.CreateAsync(absence));
        }

        [Fact]
        public void Update_WithoutACid_ShouldKeepTheStoredOne()
        {
            EnsureEncryptionKeyInitialized();
            var stored = BuildAbsence(EnumEmployeeAbsenceType.Atestado);
            stored.Cid = SharedFunctions.Encrypt(PlainCid);
            var (sut, _) = BuildSut([stored]);

            var updated = sut.Update(new EmployeeAbsence
            {
                Id = stored.Id,
                EmployeeId = stored.EmployeeId,
                EnterpriseId = stored.EnterpriseId,
                Type = (int)EnumEmployeeAbsenceType.Atestado,
                StartDate = stored.StartDate,
                EndDate = stored.EndDate,
                IsJustified = false,
                IsActive = true
            });

            Assert.Equal(stored.Cid, updated.Cid);
            Assert.False(updated.IsJustified);
        }

        [Fact]
        public void Update_ChangingTheTypeAwayFromAtestado_ShouldDropTheStoredCid()
        {
            EnsureEncryptionKeyInitialized();
            var stored = BuildAbsence(EnumEmployeeAbsenceType.Atestado);
            stored.Cid = SharedFunctions.Encrypt(PlainCid);
            var (sut, _) = BuildSut([stored]);

            var updated = sut.Update(new EmployeeAbsence
            {
                Id = stored.Id,
                EmployeeId = stored.EmployeeId,
                EnterpriseId = stored.EnterpriseId,
                Type = (int)EnumEmployeeAbsenceType.Falta,
                StartDate = stored.StartDate,
                EndDate = stored.EndDate,
                IsJustified = false,
                IsActive = true
            });

            Assert.Null(updated.Cid);
        }

        [Fact]
        public void Delete_ShouldSoftDelete()
        {
            var stored = BuildAbsence(EnumEmployeeAbsenceType.Falta);
            var (sut, _) = BuildSut([stored]);

            var deleted = sut.Delete(stored.Id);

            Assert.False(deleted.IsActive);
        }

        private static EmployeeAbsence BuildAbsence(EnumEmployeeAbsenceType type)
        {
            return new EmployeeAbsence
            {
                Id = Guid.NewGuid(),
                EmployeeId = Guid.NewGuid(),
                EnterpriseId = Guid.NewGuid(),
                Type = (int)type,
                StartDate = new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc),
                IsJustified = type != EnumEmployeeAbsenceType.Falta,
                IsActive = true
            };
        }

        private static (EmployeeAbsenceService sut, List<EmployeeAbsence> store) BuildSut(List<EmployeeAbsence>? stored = null)
        {
            var store = stored ?? [];
            var repository = new Mock<IRepository<EmployeeAbsence>>();

            repository.Setup(x => x.CreateAsync(It.IsAny<EmployeeAbsence>()))
                .ReturnsAsync((EmployeeAbsence absence) =>
                {
                    store.Add(absence);
                    return absence;
                });

            repository.Setup(x => x.GetById(It.IsAny<Guid>()))
                .Returns((Guid id) => store.FirstOrDefault(x => x.Id == id)!);

            repository.Setup(x => x.Update(It.IsAny<EmployeeAbsence>()))
                .Returns((EmployeeAbsence absence) => absence);

            repository.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            return (new EmployeeAbsenceService(repository.Object), store);
        }

        private static FieldInfo? EncryptionKeyField()
        {
            return typeof(SharedFunctions)
                .GetField("_encryptionKey", BindingFlags.NonPublic | BindingFlags.Static);
        }

        /// <summary>
        /// SharedFunctions.Initialize needs a service provider these tests have no business
        /// building, so the AES key is seeded directly — the same trick StorageObjectKeyValidationTests uses.
        /// </summary>
        private static void EnsureEncryptionKeyInitialized()
        {
            var encryptionField = EncryptionKeyField();
            if (encryptionField == null)
                return;

            if (!string.IsNullOrWhiteSpace(encryptionField.GetValue(null) as string))
                return;

            var keyBytes = Enumerable.Range(1, 32).Select(index => (byte)index).ToArray();
            encryptionField.SetValue(null, Convert.ToBase64String(keyBytes));
        }
    }
}
