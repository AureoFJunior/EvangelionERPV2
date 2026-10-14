using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261014120000_AddProductEnterpriseVariantGroupIndex")]
    public partial class AddProductEnterpriseVariantGroupIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_Product_EnterpriseId_VariantGroupId'
                      AND object_id = OBJECT_ID(N'[dbo].[Product]'))
                BEGIN
                    CREATE INDEX [IX_Product_EnterpriseId_VariantGroupId]
                    ON [dbo].[Product] ([EnterpriseId], [VariantGroupId]);
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_Product_EnterpriseId_VariantGroupId'
                      AND object_id = OBJECT_ID(N'[dbo].[Product]'))
                BEGIN
                    DROP INDEX [IX_Product_EnterpriseId_VariantGroupId] ON [dbo].[Product];
                END
                """);
        }
    }
}
