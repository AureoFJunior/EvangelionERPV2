using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Context
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Customer> Customer { get; set; }
        public DbSet<Enterprise> Enterprise { get; set; }
        public DbSet<Email> Email { get; set; }
        public DbSet<Order> Order { get; set; }
        public DbSet<OrderedProduct> OrderedProduct { get; set; }
        public DbSet<Product> Product { get; set; }
        public DbSet<User> User { get; set; }
    }
}
