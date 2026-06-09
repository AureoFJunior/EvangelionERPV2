using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingChanges_20260414 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_Email_EnterpriseId'
                      AND object_id = OBJECT_ID(N'[Email]')
                )
                BEGIN
                    DROP INDEX [IX_Email_EnterpriseId] ON [Email];
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_Email_EnterpriseId'
                      AND object_id = OBJECT_ID(N'[Email]')
                )
                BEGIN
                    CREATE INDEX [IX_Email_EnterpriseId] ON [Email] ([EnterpriseId]);
                END
                """);
        }
    }
}
