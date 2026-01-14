using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.OrderModule.Infra.Context
{
    public class OrderModuleDbContext : DbContext
    {
        public OrderModuleDbContext(DbContextOptions<OrderModuleDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            OrderModuleDbContextIndexes.Configure(modelBuilder);
        }

        #region DbSets  
        public DbSet<Order> Order { get; set; }
        public DbSet<OrderedProduct> OrderedProduct { get; set; }
        public DbSet<Product> Product { get; set; }
        public DbSet<User> User { get; set; }
        #endregion
    }
}
