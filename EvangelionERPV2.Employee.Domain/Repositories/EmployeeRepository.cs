using EvangelionERPV2.EmployeeModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.EmployeeModule.Domain.Repositories
{
    public class EmployeeRepository : Repository<Employee>, IEmployeeRepository<Employee>
    {
        public EmployeeRepository(AppDbContext context) : base(context)
        {
        }

        public async override Task<Employee> GetByIdAsync(Guid id)
        {
            IQueryable<Employee> query = _context.Set<Employee>().Where(e => e.Id == id).AsNoTracking();
            Employee? employee = await query.FirstOrDefaultAsync();

            if (employee == null)
                throw new NotFoundDatabaseException();

            return employee;
        }

        public async override Task<IEnumerable<Employee>> GetAllAsync(Expression<Func<Employee, bool>>? predicate)
        {
            IQueryable<Employee> query = _context.Set<Employee>()
                .Include(o => o.Enterprise)
                .AsNoTracking();

            if (predicate != null)
                query = query.Where(predicate);

            IEnumerable<Employee> result = await query.ToListAsync();

            if (!result.Any())
                throw new NotFoundDatabaseException();

            return result;
        }

        public async override Task<IEnumerable<Employee>> GetAllAsync(int? pageNumber, int? pageSize, Expression<Func<Employee, bool>>? predicate = null)
        {
            if (pageNumber == null || pageSize == null)
                return await GetAllAsync(predicate);

            IQueryable<Employee> query = _context.Set<Employee>()
                .Include(o => o.Enterprise)
                .AsNoTracking();

            if (predicate != null)
                query = query.Where(predicate);

            int skip = (pageNumber.Value - 1) * pageSize.Value;

            IEnumerable<Employee> result = await query
                .OrderBy(entity => entity.Id)
                .Skip(skip)
                .Take(pageSize.Value)
                .ToListAsync();

            if (!result.Any())
                throw new NotFoundDatabaseException();

            return result;
        }

        public async Task<IEnumerable<Employee>> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Employee employee,
        bool? isActive = null
        )
        {
            if (employee == null)
                throw new NotFoundDatabaseException("Empty filter with no data found.");

            const int MaxNameFilterLength = 150;
            const int MaxEmailFilterLength = 150;
            const int MaxPositionFilterLength = 100;
            const int MaxPhoneFilterLength = 30;

            Expression<Func<Employee, object>> orderBy = FillOrderByPerField(employee);

            var nameFilter = SharedFunctions.EnsureSearchFilterLength(employee.Name, MaxNameFilterLength, nameof(employee.Name));
            var emailFilter = SharedFunctions.EnsureSearchFilterLength(employee.Email, MaxEmailFilterLength, nameof(employee.Email));
            var positionFilter = SharedFunctions.EnsureSearchFilterLength(employee.Position, MaxPositionFilterLength, nameof(employee.Position));
            var phoneFilter = SharedFunctions.EnsureSearchFilterLength(employee.PhoneNumber, MaxPhoneFilterLength, nameof(employee.PhoneNumber));
            var escapedNameFilter = SharedFunctions.EscapeLikePattern(nameFilter ?? string.Empty);
            var escapedEmailFilter = SharedFunctions.EscapeLikePattern(emailFilter ?? string.Empty);
            var escapedPositionFilter = SharedFunctions.EscapeLikePattern(positionFilter ?? string.Empty);
            var escapedPhoneFilter = SharedFunctions.EscapeLikePattern(phoneFilter ?? string.Empty);

            return await this.GetAllAsyncByFilter(
            descending,
            pageNumber,
            pageSize,
            x =>
            (employee.EnterpriseId == Guid.Empty || x.EnterpriseId == employee.EnterpriseId)
            && (!isActive.HasValue || x.IsActive == isActive.Value)
            && (string.IsNullOrEmpty(nameFilter) || EF.Functions.Like(x.Name, $"%{escapedNameFilter}%", "\\"))
            && (string.IsNullOrEmpty(emailFilter) || EF.Functions.Like(x.Email, $"%{escapedEmailFilter}%", "\\"))
            && (string.IsNullOrEmpty(positionFilter) || EF.Functions.Like(x.Position, $"%{escapedPositionFilter}%", "\\"))
            && (string.IsNullOrEmpty(phoneFilter) || EF.Functions.Like(x.PhoneNumber, $"%{escapedPhoneFilter}%", "\\")),
            orderBy
            );
        }

        private static Expression<Func<Employee, object>> FillOrderByPerField(Employee employee)
        {
            if (employee.Id != Guid.Empty)
                return x => x.Id;
            else if (!string.IsNullOrEmpty(employee.Name))
                return x => x.Name;
            else if (!string.IsNullOrEmpty(employee.Email))
                return x => x.Email;
            else if (!string.IsNullOrEmpty(employee.Position))
                return x => x.Position;
            else if (!string.IsNullOrEmpty(employee.PhoneNumber))
                return x => x.PhoneNumber;
            else if (employee.CreatedAt != DateTime.MinValue)
                return x => x.CreatedAt;
            else if (employee.UpdatedAt.HasValue && employee.UpdatedAt.Value != DateTime.MinValue)
                return x => x.UpdatedAt ?? DateTime.MinValue;

            // An all-empty filter is a plain "list the enterprise's employees" call: fall back to
            // the primary key so it pages deterministically instead of failing.
            return x => x.Id;
        }
    }
}
