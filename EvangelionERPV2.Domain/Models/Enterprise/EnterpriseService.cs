using EvangelionERPV2.Domain.Exceptions;
using EvangelionERPV2.Domain.Interfaces.Repositories;
using EvangelionERPV2.Domain.Interfaces.Services;

namespace EvangelionERPV2.Domain.Models
{
    public class EnterpriseService : IEnterpriseService<Enterprise>
    {
        private readonly IRepository<Enterprise> _enterpriseRepository;

        public EnterpriseService(IRepository<Enterprise> enterpriseRepository)
        {
            _enterpriseRepository = enterpriseRepository;
        }

        public async Task<Enterprise> CreateAsync(Enterprise enterprise)
        {
            var existentEnterprise = _enterpriseRepository.GetById(enterprise.Id);
            Enterprise includedEnterprise = new Enterprise();

            if (existentEnterprise == null)
            {
                includedEnterprise = await _enterpriseRepository.CreateAsync(enterprise);
                await _enterpriseRepository.CommitAsync();
                return includedEnterprise;
            }
            throw new InsertDatabaseException();
        }

        public Enterprise Update(Enterprise enterprise)
        {
            Enterprise existentEnterprise = _enterpriseRepository.GetById(enterprise.Id);
            Enterprise updatedEnterprise = new Enterprise();

            if (existentEnterprise != null)
            {
                enterprise.UpdatedAt = DateTime.UtcNow;
                updatedEnterprise = _enterpriseRepository.Update(enterprise);
                _enterpriseRepository.Commit();
                return updatedEnterprise;
            }

            throw new NotFoundDatabaseException();
        }

        public Enterprise Delete(Guid id)
        {

            Enterprise enterprise = _enterpriseRepository.GetById(id);
            Enterprise deletedEnterprise = new Enterprise();

            if (enterprise != null)
            {
                enterprise.IsActive = false;
                enterprise.UpdatedAt = DateTime.UtcNow;
                deletedEnterprise = _enterpriseRepository.Update(enterprise);
                _enterpriseRepository.Commit();
                return deletedEnterprise;
            }
            throw new NotFoundDatabaseException();
        }

        /// <summary>
        /// Send emails with the monthly bills for the customers that have an email registred and billing module active
        /// </summary>
        public void SendMonthlyBilling(DateTime initialDate, DateTime endDate)
        {
            // verify if have email and billing module is active

            // if is automatic - get the bills of the current month
            // else - get the biils in the range

            //
        }
    }
}