using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.EnterpriseModule.Infra.Context
{
    public class EnterpriseModuleDbContext : DbContext
    {
        public EnterpriseModuleDbContext(DbContextOptions<EnterpriseModuleDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            EnterpriseModuleDbContextIndexes.Configure(modelBuilder);
        }

        #region DbSets
        public DbSet<Enterprise> Enterprise { get; set; }
        #endregion
    }
}
