using EvangelionERPV2.CustomerModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.CustomerModule.Domain.Repositories
{
    public class CustomerRepository : Repository<Customer>, ICustomerRepository<Customer>
    {
        public CustomerRepository(AppDbContext context) : base(context)
        {
        }

        public async override Task<Customer> GetByIdAsync(Guid id)
        {
            IQueryable<Customer> query = _context.Set<Customer>().Where(e => e.Id == id).AsNoTracking();
            Customer? customer = await query.FirstOrDefaultAsync();

            if (customer == null)
                throw new NotFoundDatabaseException();

            return customer;
        }

        public async override Task<IEnumerable<Customer>> GetAllAsync(Func<Customer, bool>? predicate)
        {
            IQueryable<Customer> query = _context.Set<Customer>()
                .Include(o => o.Enterprise)
                .AsNoTracking();

            IEnumerable<Customer> result = predicate != null
                ? query.AsEnumerable().Where(predicate).ToList()
                : await query.ToListAsync();

            if (!result.Any())
                throw new NotFoundDatabaseException();

            return result;
        }

        public async override Task<IEnumerable<Customer>> GetAllAsync(int? pageNumber, int? pageSize, Func<Customer, bool>? predicate = null)
        {
            if (pageNumber == null || pageSize == null)
                return await GetAllAsync(predicate);

            IQueryable<Customer> query = _context.Set<Customer>()
                .Include(o => o.Enterprise)
                .AsNoTracking();
            int skip = (pageNumber.Value - 1) * pageSize.Value;

            IEnumerable<Customer> result = predicate != null
                ? query.AsEnumerable().Where(predicate).Skip(skip).Take(pageSize.Value).ToList()
                : await query.Skip(skip).Take(pageSize.Value).ToListAsync();

            if (!result.Any())
                throw new NotFoundDatabaseException();

            return result;
        }

        public async Task<IEnumerable<Customer>> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Customer customer
        )
        {
            if (customer == null)
                throw new NotFoundDatabaseException("Empty filter with no data found.");

            Expression<Func<Customer, object>> orderBy = FillOrderByPerField(customer);

            var nameFilter = customer.Name?.Trim();
            var emailFilter = customer.Email?.Trim();
            var documentFilter = customer.Document?.Trim();
            var phoneFilter = customer.PhoneNumber?.Trim();

            return await this.GetAllAsyncByFilter(
            descending,
            pageNumber,
            pageSize,
            x =>
            (customer.EnterpriseId == Guid.Empty || x.EnterpriseId == customer.EnterpriseId)
            && (string.IsNullOrEmpty(nameFilter) || EF.Functions.Like(x.Name, $"%{nameFilter}%"))
            && (string.IsNullOrEmpty(emailFilter) || EF.Functions.Like(x.Email, $"%{emailFilter}%"))
            && (string.IsNullOrEmpty(documentFilter) || (x.Document != null && EF.Functions.Like(x.Document, $"%{documentFilter}%")))
            && (string.IsNullOrEmpty(phoneFilter) || EF.Functions.Like(x.PhoneNumber, $"%{phoneFilter}%")),
            orderBy
            );
        }

        private static Expression<Func<Customer, object>> FillOrderByPerField(Customer customer)
        {
            if (customer.Id != Guid.Empty)
                return x => x.Id;
            else if (!string.IsNullOrEmpty(customer.Name))
                return x => x.Name;
            else if (!string.IsNullOrEmpty(customer.Email))
                return x => x.Email;
            else if (!string.IsNullOrEmpty(customer.Adress))
                return x => x.Adress;
            else if (!string.IsNullOrEmpty(customer.PhoneNumber))
                return x => x.PhoneNumber;
            else if (customer.CreatedAt != DateTime.MinValue)
                return x => x.CreatedAt;
            else if (customer.UpdatedAt.HasValue && customer.UpdatedAt.Value != DateTime.MinValue)
                return x => x.UpdatedAt ?? DateTime.MinValue;

            throw new NotFoundDatabaseException("Empty filter with no data found.");
        }
    }
}
