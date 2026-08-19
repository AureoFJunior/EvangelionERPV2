using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.SupplierModule.Application.Interface;

namespace EvangelionERPV2.SupplierModule.Application.Services
{
    public class SupplierService : ISupplierService<Supplier>
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Supplier> _supplierRepository;

        public SupplierService(EvangelionERPV2.Shared.Repositories.IRepository<Supplier> supplierRepository)
        {
            _supplierRepository = supplierRepository;
        }

        public async Task<Supplier> CreateAsync(Supplier supplier)
        {
            try
            {
                // The incoming Id is client input, never an identity: the server assigns its own.
                supplier.Id = Guid.NewGuid();
                Supplier includedSupplier = await _supplierRepository.CreateAsync(supplier);
                await _supplierRepository.CommitAsync();
                return includedSupplier;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while creating supplier.", ex);
            }
        }

        public Supplier Update(Supplier supplier)
        {
            try
            {
                Supplier existentSupplier = _supplierRepository.GetById(supplier.Id);

                if (existentSupplier == null)
                    throw new NotFoundDatabaseException($"{nameof(Supplier)} was not found in database.");

                supplier.UpdatedAt = DateTime.UtcNow;
                Supplier updatedSupplier = _supplierRepository.Update(supplier);
                _supplierRepository.Commit();
                return updatedSupplier;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while updating supplier.", ex);
            }
        }

        public Supplier Delete(Guid id)
        {
            try
            {
                Supplier supplier = _supplierRepository.GetById(id);

                if (supplier == null)
                    throw new NotFoundDatabaseException($"{nameof(Supplier)} was not found in database.");

                // Soft delete only: payable bills keep pointing at the supplier that issued them,
                // so the row has to survive for the payables history to stay readable.
                supplier.IsActive = false;
                supplier.UpdatedAt = DateTime.UtcNow;
                Supplier deletedSupplier = _supplierRepository.Update(supplier);
                _supplierRepository.Commit();
                return deletedSupplier;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while deleting supplier.", ex);
            }
        }
    }
}
