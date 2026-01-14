using EvangelionERPV2.EnterpriseModule.Application.Interface;
using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;

namespace EvangelionERPV2.EnterpriseModule.Application.Services
{
    public class EnterpriseService : IEnterpriseService<Enterprise>
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> _enterpriseRepository;

        public EnterpriseService(EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> enterpriseRepository)
        {
            _enterpriseRepository = enterpriseRepository;
        }

        public async Task<Enterprise> CreateAsync(Enterprise enterprise)
        {
            try
            {
                var existentEnterprise = _enterpriseRepository.GetById(enterprise.Id);
                Enterprise includedEnterprise = new Enterprise();

                if (existentEnterprise != null)
                    throw new InsertDatabaseException($"{nameof(Enterprise)} already has an register in database");

                includedEnterprise = await _enterpriseRepository.CreateAsync(enterprise);
                await _enterpriseRepository.CommitAsync();
                return includedEnterprise;

            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
            }
        }

        public Enterprise Update(Enterprise enterprise)
        {
            try
            {
                Enterprise existentEnterprise = _enterpriseRepository.GetById(enterprise.Id);
                Enterprise updatedEnterprise = new Enterprise();

                if (existentEnterprise == null)
                    throw new NotFoundDatabaseException($"{nameof(Enterprise)} was not found in database.");

                enterprise.UpdatedAt = DateTime.UtcNow;
                updatedEnterprise = _enterpriseRepository.Update(enterprise);
                _enterpriseRepository.Commit();
                return updatedEnterprise;
            }
            catch (NotFoundDatabaseException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
            }
        }

        public Enterprise Delete(Guid id)
        {
            try
            {
                Enterprise enterprise = _enterpriseRepository.GetById(id);
                Enterprise deletedEnterprise = new Enterprise();

                if (enterprise == null)
                    throw new NotFoundDatabaseException($"{nameof(Enterprise)} was not found in database.");

                enterprise.IsActive = false;
                enterprise.UpdatedAt = DateTime.UtcNow;
                deletedEnterprise = _enterpriseRepository.Update(enterprise);
                _enterpriseRepository.Commit();
                return deletedEnterprise;
            }
            catch (NotFoundDatabaseException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
            }
        }
    }
}