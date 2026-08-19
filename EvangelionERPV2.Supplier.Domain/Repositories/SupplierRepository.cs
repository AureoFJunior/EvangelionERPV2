using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.SupplierModule.Domain.Interface;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace EvangelionERPV2.SupplierModule.Domain.Repositories
{
    public class SupplierRepository : Repository<Supplier>, ISupplierRepository<Supplier>
    {
        public SupplierRepository(AppDbContext context) : base(context)
        {
        }

        public async override Task<Supplier> GetByIdAsync(Guid id)
        {
            IQueryable<Supplier> query = _context.Set<Supplier>().Where(e => e.Id == id).AsNoTracking();
            Supplier? supplier = await query.FirstOrDefaultAsync();

            if (supplier == null)
                throw new NotFoundDatabaseException();

            return supplier;
        }

        public async override Task<IEnumerable<Supplier>> GetAllAsync(Expression<Func<Supplier, bool>>? predicate)
        {
            IQueryable<Supplier> query = _context.Set<Supplier>()
                .Include(o => o.Enterprise)
                .AsNoTracking();

            if (predicate != null)
                query = query.Where(predicate);

            IEnumerable<Supplier> result = await query.ToListAsync();

            if (!result.Any())
                throw new NotFoundDatabaseException();

            return result;
        }

        public async override Task<IEnumerable<Supplier>> GetAllAsync(int? pageNumber, int? pageSize, Expression<Func<Supplier, bool>>? predicate = null)
        {
            if (pageNumber == null || pageSize == null)
                return await GetAllAsync(predicate);

            IQueryable<Supplier> query = _context.Set<Supplier>()
                .Include(o => o.Enterprise)
                .AsNoTracking();

            if (predicate != null)
                query = query.Where(predicate);

            int skip = (pageNumber.Value - 1) * pageSize.Value;

            IEnumerable<Supplier> result = await query
                .OrderBy(entity => entity.Id)
                .Skip(skip)
                .Take(pageSize.Value)
                .ToListAsync();

            if (!result.Any())
                throw new NotFoundDatabaseException();

            return result;
        }

        public async Task<IEnumerable<Supplier>> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Supplier supplier
        )
        {
            if (supplier == null)
                throw new NotFoundDatabaseException("Empty filter with no data found.");

            const int MaxNameFilterLength = 150;
            const int MaxEmailFilterLength = 150;
            const int MaxDocumentFilterLength = 30;
            const int MaxPhoneFilterLength = 30;

            Expression<Func<Supplier, object>> orderBy = FillOrderByPerField(supplier);

            var nameFilter = SharedFunctions.EnsureSearchFilterLength(supplier.Name, MaxNameFilterLength, nameof(supplier.Name));
            var emailFilter = SharedFunctions.EnsureSearchFilterLength(supplier.Email, MaxEmailFilterLength, nameof(supplier.Email));
            var documentFilter = SharedFunctions.EnsureSearchFilterLength(supplier.Document, MaxDocumentFilterLength, nameof(supplier.Document));
            var phoneFilter = SharedFunctions.EnsureSearchFilterLength(supplier.PhoneNumber, MaxPhoneFilterLength, nameof(supplier.PhoneNumber));
            var escapedNameFilter = SharedFunctions.EscapeLikePattern(nameFilter ?? string.Empty);
            var escapedEmailFilter = SharedFunctions.EscapeLikePattern(emailFilter ?? string.Empty);
            var escapedDocumentFilter = SharedFunctions.EscapeLikePattern(documentFilter ?? string.Empty);
            var escapedPhoneFilter = SharedFunctions.EscapeLikePattern(phoneFilter ?? string.Empty);

            return await this.GetAllAsyncByFilter(
            descending,
            pageNumber,
            pageSize,
            x =>
            (supplier.EnterpriseId == Guid.Empty || x.EnterpriseId == supplier.EnterpriseId)
            && (string.IsNullOrEmpty(nameFilter) || EF.Functions.Like(x.Name, $"%{escapedNameFilter}%", "\\"))
            && (string.IsNullOrEmpty(emailFilter) || EF.Functions.Like(x.Email, $"%{escapedEmailFilter}%", "\\"))
            && (string.IsNullOrEmpty(documentFilter) || (x.Document != null && EF.Functions.Like(x.Document, $"%{escapedDocumentFilter}%", "\\")))
            && (string.IsNullOrEmpty(phoneFilter) || EF.Functions.Like(x.PhoneNumber, $"%{escapedPhoneFilter}%", "\\")),
            orderBy
            );
        }

        private static Expression<Func<Supplier, object>> FillOrderByPerField(Supplier supplier)
        {
            if (supplier.Id != Guid.Empty)
                return x => x.Id;
            else if (!string.IsNullOrEmpty(supplier.Name))
                return x => x.Name;
            else if (!string.IsNullOrEmpty(supplier.Email))
                return x => x.Email;
            else if (!string.IsNullOrEmpty(supplier.Adress))
                return x => x.Adress;
            else if (!string.IsNullOrEmpty(supplier.PhoneNumber))
                return x => x.PhoneNumber;
            else if (supplier.CreatedAt != DateTime.MinValue)
                return x => x.CreatedAt;
            else if (supplier.UpdatedAt.HasValue && supplier.UpdatedAt.Value != DateTime.MinValue)
                return x => x.UpdatedAt ?? DateTime.MinValue;

            // An all-empty filter is a plain "list the enterprise's suppliers" call: fall back to
            // the primary key so it pages deterministically instead of failing.
            return x => x.Id;
        }
    }
}
