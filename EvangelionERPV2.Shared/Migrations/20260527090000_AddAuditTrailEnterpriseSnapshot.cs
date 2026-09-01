using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260527090000_AddAuditTrailEnterpriseSnapshot")]
    public partial class AddAuditTrailEnterpriseSnapshot : Migration
    {
        // PRE-FLIGHT: this migration was undiscoverable (no [Migration], no .Designer.cs) until
        // the attribute above was added, so it has never been recorded in __EFMigrationsHistory
        // anywhere. If an environment acquired [AuditTrails].[EnterpriseId] by hand-applied SQL,
        // AddColumn below fails and aborts the whole run. Check first:
        //
        //   SELECT COL_LENGTH('AuditTrails', 'EnterpriseId');   -- must be NULL before applying
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EnterpriseId",
                table: "AuditTrails",
                type: "uniqueidentifier",
                nullable: true);

            // Through EXEC: the idempotent script emits this in the same batch as the ALTER that
            // adds the column, and SQL Server compiles a batch whole — naming the column directly
            // fails with "Invalid column name" on any database that has not run this migration yet.
            migrationBuilder.Sql("""
                EXEC(N'
                    UPDATE [audit]
                    SET [EnterpriseId] = [userRecord].[EnterpriseId]
                    FROM [AuditTrails] AS [audit]
                    INNER JOIN [User] AS [userRecord]
                        ON [audit].[UserId] = [userRecord].[Id]
                    WHERE [audit].[EnterpriseId] IS NULL;

                ');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AuditTrails_EnterpriseId_ChangedAt",
                table: "AuditTrails",
                columns: new[] { "EnterpriseId", "ChangedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditTrails_EnterpriseId_ChangedAt",
                table: "AuditTrails");

            migrationBuilder.DropColumn(
                name: "EnterpriseId",
                table: "AuditTrails");
        }
    }
}
