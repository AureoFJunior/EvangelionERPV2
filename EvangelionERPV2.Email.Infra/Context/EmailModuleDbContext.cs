using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.EmailModule.Infra.Context
{
    public class EmailModuleDbContext : DbContext
    {
        public EmailModuleDbContext(DbContextOptions<EmailModuleDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            EmailModuleDbContextIndexes.Configure(modelBuilder);
        }

        #region DbSets
        public DbSet<Email> Email { get; set; }
        #endregion
    }
}
