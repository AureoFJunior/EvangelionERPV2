using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.CustomerModule.Infra.Context
{
    public class CustomerModuleDbContext : DbContext
    {
        public CustomerModuleDbContext(DbContextOptions<CustomerModuleDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            CustomerModuleDbContextIndexes.Configure(modelBuilder);
        }

        #region DbSets
        public DbSet<Enterprise> Enterprise { get; set; }
        public DbSet<Customer> Customer { get; set; }
        #endregion
    }
}
