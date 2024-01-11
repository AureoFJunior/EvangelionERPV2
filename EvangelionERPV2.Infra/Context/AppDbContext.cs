using EvangelionERPV2.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Infra.Context
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            AppDbContextIndexes.Configure(modelBuilder);
        }

        #region DbSets
        public DbSet<User> User { get; set; }
        #endregion
    }
}
