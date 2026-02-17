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
        public DbSet<Bill> Bill { get; set; }
        public DbSet<NFeDocument> NFeDocument { get; set; }
        public DbSet<Order> Order { get; set; }
        public DbSet<OrderedProduct> OrderedProduct { get; set; }
        public DbSet<Product> Product { get; set; }
        public DbSet<PayableBill> PayableBill { get; set; }
        public DbSet<ForecastSimulationLog> ForecastSimulationLog { get; set; }
        public DbSet<RefreshToken> RefreshToken { get; set; }
        public DbSet<User> User { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Enterprise>()
                .Property(enterprise => enterprise.Currency)
                .HasMaxLength(3)
                .HasDefaultValue("BRL");

            modelBuilder.Entity<Bill>()
                .ToTable("Boleto");

            modelBuilder.Entity<NFeDocument>()
                .Property(document => document.AccessKey)
                .HasMaxLength(44);
        }
    }
}
