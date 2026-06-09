using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    public partial class MakeActiveUserEmailGloballyUnique : Migration
    {
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
                    CREATE UNIQUE INDEX [IX_User_NormalizedActiveEmail_Active]
                    ON [User] ([NormalizedActiveEmail])
                    WHERE [IsActive] = 1 AND [NormalizedActiveEmail] IS NOT NULL;
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
