using EvangelionERPV2.Shared.Context;

namespace EvangelionERPV2.Shared.Repositories
{
    public class UnitOfWork<TContext> : UnitOfWorkBase, IUnitOfWork<TContext> where TContext : AppDbContext
    {
        public UnitOfWork(AppDbContext context) : base(context)
        {
        }
    }
}
