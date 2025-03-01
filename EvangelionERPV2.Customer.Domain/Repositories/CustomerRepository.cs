using EvangelionERPV2.CustomerModule.Domain.Interface;
using EvangelionERPV2.CustomerModule.Infra.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace EvangelionERPV2.CustomerModule.Domain.Repositories
{
    public class CustomerRepository : Repository<Customer>, ICustomerRepository<Customer>
    {
        public CustomerRepository(CustomerModuleDbContext context) : base(context)
        {
        }

        public async override Task<Customer> GetByIdAsync(Guid id)
        {
            try
            {
                IQueryable<Customer> query = _context.Set<Customer>().Where(e => e.Id == id).AsNoTracking();

                if (await query.AnyAsync())
                    return await query.FirstOrDefaultAsync();

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }

        public async override Task<IEnumerable<Customer>> GetAllAsync(Func<Customer, bool> predicate)
        {
            try
            {
                IQueryable<Customer> query = _context.Set<Customer>().AsNoTracking();

                if (predicate != null)
                    return _context.Set<Customer>().AsNoTracking().Where(predicate);

                if (await query.AnyAsync())
                    return await query.ToListAsync();

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }

        public async override Task<IEnumerable<Customer>> GetAllAsync(int? pageNumber, int? pageSize, Func<Customer, bool> predicate = null)
        {
            try
            {
                if (pageNumber == null || pageSize == null)
                    return await GetAllAsync(predicate);

                IQueryable<Customer> query = _context.Set<Customer>().AsNoTracking();
                int skip = (pageNumber - 1) * pageSize ?? 1;

                if (predicate != null)
                    return _context.Set<Customer>().AsNoTracking().Where(predicate).Skip(skip).Take(pageSize ?? 0);

                List<Customer>? result = null;

                if (await query.AnyAsync())
                    result = await query.Skip(skip).Take(pageSize ?? 0).ToListAsync();

                if (result?.Any() == false)
                    return result;

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }

        public async Task<IEnumerable<Customer>> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Customer customer
        )
        {
            try
            {
                Expression<Func<Customer, object>> orderBy = null;

                if (customer == null)
                    throw new NotFoundDatabaseException("Empty filter with no data found.");

                orderBy = FillOrderByPerField(customer, orderBy);

                return await this.GetAllAsyncByFilter(
                descending,
                pageNumber,
                pageSize,
                x =>
                string.IsNullOrEmpty(customer.Name) || x.Name == customer.Name ||
                customer.EnterpriseId == Guid.Empty || x.EnterpriseId == customer.EnterpriseId
                ||
                (string.IsNullOrEmpty(customer.Name) || x.Name == customer.Name) && (customer.EnterpriseId == Guid.Empty || x.EnterpriseId == customer.EnterpriseId)
                ,
                orderBy
                );
            }
            catch (Exception ex) { throw; }
        }

        private static Expression<Func<Customer, object>> FillOrderByPerField(Customer customer, Expression<Func<Customer, object>> orderBy)
        {
            if (customer.Id != null && customer.Id != Guid.Empty)
                return x => x.Id;
            else if (!string.IsNullOrEmpty(customer.Name))
                return x => x.Name;
            else if (!string.IsNullOrEmpty(customer.Email))
                return x => x.Email;
            else if (!string.IsNullOrEmpty(customer.Adress))
                return x => x.Adress;
            else if (!string.IsNullOrEmpty(customer.PhoneNumber))
                return x => x.PhoneNumber;
            else if (customer.CreatedAt != null && customer.CreatedAt != DateTime.MinValue)
                return x => x.CreatedAt;
            else if (customer.UpdatedAt != null && customer.UpdatedAt != DateTime.MinValue)
                return x => x.UpdatedAt;

            throw new NotFoundDatabaseException("Empty filter with no data found.");
        }
    }
}