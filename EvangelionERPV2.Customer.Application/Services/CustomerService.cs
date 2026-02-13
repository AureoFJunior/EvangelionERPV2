using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.CustomerModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;

namespace EvangelionERPV2.CustomerModule.Application.Services
{
    public class CustomerService : ICustomerService<Customer>
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Customer> _customerRepository;

        public CustomerService(EvangelionERPV2.Shared.Repositories.IRepository<Customer> customerRepository)
        {
            _customerRepository = customerRepository;
        }

        public async Task<Customer> CreateAsync(Customer customer)
        {
            try
            {
                var existentCustomer = _customerRepository.GetById(customer.Id);
                Customer includedCustomer = new Customer();

                if (existentCustomer != null)
                    throw new InsertDatabaseException($"{nameof(Customer)} already has an register in database");

                includedCustomer = await _customerRepository.CreateAsync(customer);
                await _customerRepository.CommitAsync();
                return includedCustomer;

            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex);
            }
        }

        public Customer Update(Customer customer)
        {
            try
            {
                Customer existentCustomer = _customerRepository.GetById(customer.Id);
                Customer updatedCustomer = new Customer();

                if (existentCustomer == null)
                    throw new NotFoundDatabaseException($"{nameof(Customer)} was not found in database.");

                customer.UpdatedAt = DateTime.UtcNow;
                updatedCustomer = _customerRepository.Update(customer);
                _customerRepository.Commit();
                return updatedCustomer;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex);
            }
        }

        public Customer Delete(Guid id)
        {
            try
            {
                Customer customer = _customerRepository.GetById(id);
                Customer deletedCustomer = new Customer();

                if (customer == null)
                    throw new NotFoundDatabaseException($"{nameof(Customer)} was not found in database.");

                customer.IsActive = false;
                customer.UpdatedAt = DateTime.UtcNow;
                deletedCustomer = _customerRepository.Update(customer);
                _customerRepository.Commit();
                return deletedCustomer;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex);
            }
        }
    }
}
