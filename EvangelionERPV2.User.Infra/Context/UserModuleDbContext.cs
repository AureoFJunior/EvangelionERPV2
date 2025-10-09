using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.UserModule.Infra.Context
{
    public class UserModuleDbContext : DbContext
    {
        public UserModuleDbContext(DbContextOptions<UserModuleDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            UserModuleDbContextIndexes.Configure(modelBuilder);
        }

        #region DbSets
        public DbSet<User> User { get; set; }
        public DbSet<Enterprise> Enterprise { get; set; }
        #endregion
    }
}
