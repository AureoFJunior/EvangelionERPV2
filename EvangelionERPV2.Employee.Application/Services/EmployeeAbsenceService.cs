using EvangelionERPV2.EmployeeModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.EmployeeModule.Application.Services
{
    public class EmployeeAbsenceService : IEmployeeAbsenceService<EmployeeAbsence>
    {
        private readonly IRepository<EmployeeAbsence> _absenceRepository;

        public EmployeeAbsenceService(IRepository<EmployeeAbsence> absenceRepository)
        {
            _absenceRepository = absenceRepository;
        }

        public async Task<EmployeeAbsence> CreateAsync(EmployeeAbsence absence, string? plainCid = null)
        {
            if (absence == null)
                throw new InsertDatabaseException($"{nameof(EmployeeAbsence)} is null.");

            try
            {
                Normalize(absence);

                absence.Id = Guid.NewGuid();
                absence.Cid = ProtectCid(absence.Type, plainCid);

                EmployeeAbsence includedAbsence = await _absenceRepository.CreateAsync(absence);
                await _absenceRepository.CommitAsync();
                return includedAbsence;
            }
            catch (InsertDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while creating employee absence.", ex);
            }
        }

        public EmployeeAbsence Update(EmployeeAbsence absence, string? plainCid = null)
        {
            if (absence == null)
                throw new InsertDatabaseException($"{nameof(EmployeeAbsence)} is null.");

            try
            {
                Normalize(absence);

                EmployeeAbsence existentAbsence = _absenceRepository.GetById(absence.Id);
                if (existentAbsence == null)
                    throw new NotFoundDatabaseException($"{nameof(EmployeeAbsence)} was not found in database.");

                absence.Cid = string.IsNullOrWhiteSpace(plainCid)
                    ? (absence.Type == (int)EnumEmployeeAbsenceType.Atestado ? existentAbsence.Cid : null)
                    : ProtectCid(absence.Type, plainCid);

                absence.UpdatedAt = DateTime.UtcNow;
                EmployeeAbsence updatedAbsence = _absenceRepository.Update(absence);
                _absenceRepository.Commit();
                return updatedAbsence;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (InsertDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while updating employee absence.", ex);
            }
        }

        public EmployeeAbsence Delete(Guid id)
        {
            try
            {
                EmployeeAbsence absence = _absenceRepository.GetById(id);
                if (absence == null)
                    throw new NotFoundDatabaseException($"{nameof(EmployeeAbsence)} was not found in database.");

                absence.IsActive = false;
                absence.UpdatedAt = DateTime.UtcNow;
                EmployeeAbsence deletedAbsence = _absenceRepository.Update(absence);
                _absenceRepository.Commit();
                return deletedAbsence;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while deleting employee absence.", ex);
            }
        }

        /// <summary>
        /// Server-side invariants for the fields payroll does arithmetic on. The validator checks
        /// the same things at the edge; these are re-checked here because a wrong day range does
        /// not fail loudly — it silently mispays someone.
        /// </summary>
        private static void Normalize(EmployeeAbsence absence)
        {
            if (absence.EmployeeId == Guid.Empty)
                throw new InsertDatabaseException("Employee absence must belong to an employee.");

            if (!Enum.IsDefined(typeof(EnumEmployeeAbsenceType), absence.Type))
                throw new InsertDatabaseException("Employee absence type is invalid.");

            absence.StartDate = absence.StartDate.Date;
            absence.EndDate = absence.EndDate.Date;

            if (absence.EndDate < absence.StartDate)
                throw new InsertDatabaseException("Employee absence end date cannot be before the start date.");

            absence.Reason = string.IsNullOrWhiteSpace(absence.Reason) ? null : absence.Reason.Trim();
        }

        /// <summary>
        /// Encrypts a diagnosis code with the same AES helper the rest of the codebase uses for
        /// sensitive values, and refuses to persist the row when that fails.
        /// </summary>
        private static string? ProtectCid(int type, string? plainCid)
        {
            if (string.IsNullOrWhiteSpace(plainCid))
                return null;

            if (type != (int)EnumEmployeeAbsenceType.Atestado)
                throw new InsertDatabaseException("A diagnosis code can only be recorded on an atestado.");

            var encrypted = SharedFunctions.Encrypt(plainCid.Trim());
            if (string.IsNullOrWhiteSpace(encrypted))
                throw new InsertDatabaseException("Employee absence diagnosis code could not be encrypted.");

            return encrypted;
        }
    }
}
