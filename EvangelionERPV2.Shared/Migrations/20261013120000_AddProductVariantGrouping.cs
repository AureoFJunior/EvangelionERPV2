using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261013120000_AddProductVariantGrouping")]
    public partial class AddProductVariantGrouping : Migration
    {
        // Guarded, unlike a generated migration: on 2026-10-28 a local database already had both
        // columns and the index without this row in __EFMigrationsHistory, and the plain AddColumn
        // failed with Msg 2705. Each step now checks for itself, so a database in either state ends
        // up the same.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'[dbo].[Product]', N'VariantGroupId') IS NULL
                    ALTER TABLE [dbo].[Product] ADD [VariantGroupId] uniqueidentifier NULL;
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH(N'[dbo].[Product]', N'VariantLabel') IS NULL
                    ALTER TABLE [dbo].[Product] ADD [VariantLabel] nvarchar(60) NULL;
                """);

            // EXEC: the index names columns the batch above may have just added, and SQL Server
            // compiles a batch before running it.
            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_Product_VariantGroupId_VariantLabel'
                      AND object_id = OBJECT_ID(N'[dbo].[Product]'))
                BEGIN
                    EXEC(N'CREATE UNIQUE INDEX [IX_Product_VariantGroupId_VariantLabel]
                        ON [dbo].[Product] ([VariantGroupId], [VariantLabel])
                        WHERE [IsActive] = 1 AND [VariantGroupId] IS NOT NULL');
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Product_VariantGroupId_VariantLabel",
                table: "Product");

            migrationBuilder.DropColumn(name: "VariantLabel", table: "Product");
            migrationBuilder.DropColumn(name: "VariantGroupId", table: "Product");
        }
    }
}
