using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260603130000_MakeActiveUserEmailGloballyUnique")]
    public partial class MakeActiveUserEmailGloballyUnique : Migration
    {
        // PRE-FLIGHT: this NARROWS 20260407123000's per-enterprise email uniqueness to a global
        // one. In a multi-tenant ERP the same person legitimately existing as an active user in
        // two enterprises now violates it, and neither constraint has ever actually been enforced
        // in any database (both migrations were undiscoverable until the attributes were added),
        // so real duplicates are likely. THROW 51002 below aborts the run and everything after
        // this migration — the whole Employee/payroll chain — stays unapplied. Check first:
        //
        //   SELECT UPPER(LTRIM(RTRIM([Email]))) AS NormalizedEmail, COUNT(*) AS ActiveUsers
        //   FROM [User] WHERE [IsActive] = 1 AND [Email] IS NOT NULL
        //   GROUP BY UPPER(LTRIM(RTRIM([Email]))) HAVING COUNT(*) > 1;
        //
        // Any row returned must be resolved (deactivate or re-address) before applying.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM [User]
                    WHERE [IsActive] = 1 AND [Email] IS NOT NULL
                    GROUP BY UPPER(LTRIM(RTRIM([Email])))
                    HAVING COUNT(*) > 1
                )
                BEGIN
                    THROW 51002, 'Cannot create global active email uniqueness index because duplicate active emails exist. Resolve duplicates before applying this migration.', 1;
                END;

                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] = N'IX_User_EnterpriseId_NormalizedActiveEmail_Active'
                      AND [object_id] = OBJECT_ID(N'[User]')
                )
                BEGIN
                    DROP INDEX [IX_User_EnterpriseId_NormalizedActiveEmail_Active] ON [User];
                END;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] = N'IX_User_NormalizedActiveEmail_Active'
                      AND [object_id] = OBJECT_ID(N'[User]')
                )
                BEGIN
                    -- The filter cannot reference [NormalizedActiveEmail]: SQL Server rejects
                    -- computed columns in filtered-index predicates. [Email] is NOT NULL, so
                    -- [IsActive] = 1 alone already excludes every row where the computed
                    -- value is NULL.
                    CREATE UNIQUE INDEX [IX_User_NormalizedActiveEmail_Active]
                    ON [User] ([NormalizedActiveEmail])
                    WHERE [IsActive] = 1;
                END;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] = N'IX_User_NormalizedActiveEmail_Active'
                      AND [object_id] = OBJECT_ID(N'[User]')
                )
                BEGIN
                    DROP INDEX [IX_User_NormalizedActiveEmail_Active] ON [User];
                END;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] = N'IX_User_EnterpriseId_NormalizedActiveEmail_Active'
                      AND [object_id] = OBJECT_ID(N'[User]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_User_EnterpriseId_NormalizedActiveEmail_Active]
                    ON [User] ([EnterpriseId], [NormalizedActiveEmail])
                    WHERE [IsActive] = 1 AND [EnterpriseId] IS NOT NULL;
                END;
                """);
        }
    }
}
