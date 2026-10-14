using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Test.Security
{
    public class AppDbContextModelSnapshotSyncTests
    {
        /// <summary>
        /// Migrations in this repo are written by hand (no dotnet-ef designer run), so the
        /// model snapshot is also maintained by hand. This test fails whenever an entity
        /// change lands without a matching snapshot/migration update.
        ///
        /// NOT a migration-chain guard: HasPendingModelChanges() only diffs the snapshot
        /// against the runtime model. It says nothing about whether a migration file is
        /// discoverable by EF — a migration missing its [Migration]/[DbContext] attributes
        /// (and its .Designer.cs) is silently skipped at Database.Migrate() time while this
        /// test still passes green.
        /// </summary>
        [Fact]
        public void ModelSnapshot_MatchesRuntimeModel()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer("Server=model-diff-only;Database=none;Encrypt=false")
                .Options;

            using var context = new AppDbContext(options);

            Assert.False(context.Database.HasPendingModelChanges());
        }
    }
}
