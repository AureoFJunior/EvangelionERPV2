using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    public partial class AddUniqueActiveUserIdentityIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM [User]
                    WHERE [IsActive] = 1 AND [EnterpriseId] IS NOT NULL
                    GROUP BY [EnterpriseId], UPPER(LTRIM(RTRIM([UserName])))
                    HAVING COUNT(*) > 1
                )
                BEGIN
                    THROW 51000, 'Cannot create active username uniqueness index because duplicate active usernames exist in at least one enterprise. Resolve duplicates before applying this migration.', 1;
                END;
                """);

            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM [User]
                    WHERE [IsActive] = 1 AND [EnterpriseId] IS NOT NULL
                    GROUP BY [EnterpriseId], UPPER(LTRIM(RTRIM([Email])))
                    HAVING COUNT(*) > 1
                )
                BEGIN
                    THROW 51001, 'Cannot create active email uniqueness index because duplicate active emails exist in at least one enterprise. Resolve duplicates before applying this migration.', 1;
                END;
                """);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedActiveEmail",
                table: "User",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                computedColumnSql: "CASE WHEN [IsActive] = 1 THEN CONVERT(nvarchar(450), UPPER(LTRIM(RTRIM([Email])))) END",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedActiveUserName",
                table: "User",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                computedColumnSql: "CASE WHEN [IsActive] = 1 THEN CONVERT(nvarchar(450), UPPER(LTRIM(RTRIM([UserName])))) END",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_EnterpriseId_NormalizedActiveEmail_Active",
                table: "User",
                columns: new[] { "EnterpriseId", "NormalizedActiveEmail" },
                unique: true,
                filter: "[IsActive] = 1 AND [EnterpriseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_User_EnterpriseId_NormalizedActiveUserName_Active",
                table: "User",
                columns: new[] { "EnterpriseId", "NormalizedActiveUserName" },
                unique: true,
                filter: "[IsActive] = 1 AND [EnterpriseId] IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_User_EnterpriseId_NormalizedActiveEmail_Active",
                table: "User");

            migrationBuilder.DropIndex(
                name: "IX_User_EnterpriseId_NormalizedActiveUserName_Active",
                table: "User");

            migrationBuilder.DropColumn(
                name: "NormalizedActiveEmail",
                table: "User");

            migrationBuilder.DropColumn(
                name: "NormalizedActiveUserName",
                table: "User");
        }
    }
}
