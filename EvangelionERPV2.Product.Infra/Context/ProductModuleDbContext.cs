using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.ProductModule.Infra.Context
{
    public class ProductModuleDbContext : DbContext
    {
        public ProductModuleDbContext(DbContextOptions<ProductModuleDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ProductModuleDbContextIndexes.Configure(modelBuilder);
        }

        #region DbSets
        public DbSet<OrderedProduct> OrderedProduct { get; set; }
        public DbSet<Product> Product { get; set; }
        #endregion
    }
}
