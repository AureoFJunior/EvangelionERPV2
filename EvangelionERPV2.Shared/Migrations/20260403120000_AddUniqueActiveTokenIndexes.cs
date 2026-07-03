using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueActiveTokenIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ;WITH RankedTokens AS (
                    SELECT
                        Id,
                        ROW_NUMBER() OVER (
                            PARTITION BY UserId
                            ORDER BY CreatedAt DESC, ISNULL(UpdatedAt, CreatedAt) DESC, Id DESC
                        ) AS TokenRank
                    FROM RefreshToken
                    WHERE RevokedAt IS NULL AND ExpiresAt > GETUTCDATE()
                )
                UPDATE token
                SET IsActive = CASE WHEN ranked.TokenRank = 1 THEN 1 ELSE 0 END
                FROM RefreshToken token
                INNER JOIN RankedTokens ranked ON ranked.Id = token.Id;

                UPDATE RefreshToken
                SET IsActive = 0
                WHERE RevokedAt IS NOT NULL OR ExpiresAt <= GETUTCDATE();
                """);

            migrationBuilder.Sql("""
                ;WITH RankedTokens AS (
                    SELECT
                        Id,
                        ROW_NUMBER() OVER (
                            PARTITION BY UserId
                            ORDER BY CreatedAt DESC, ISNULL(UpdatedAt, CreatedAt) DESC, Id DESC
                        ) AS TokenRank
                    FROM PasswordResetToken
                    WHERE UsedAt IS NULL AND ExpiresAt > GETUTCDATE()
                )
                UPDATE token
                SET IsActive = CASE WHEN ranked.TokenRank = 1 THEN 1 ELSE 0 END
                FROM PasswordResetToken token
                INNER JOIN RankedTokens ranked ON ranked.Id = token.Id;

                UPDATE PasswordResetToken
                SET IsActive = 0
                WHERE UsedAt IS NOT NULL OR ExpiresAt <= GETUTCDATE();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetToken_UserId_Active",
                table: "PasswordResetToken",
                column: "UserId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_UserId_Active",
                table: "RefreshToken",
                column: "UserId",
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PasswordResetToken_UserId_Active",
                table: "PasswordResetToken");

            migrationBuilder.DropIndex(
                name: "IX_RefreshToken_UserId_Active",
                table: "RefreshToken");
        }
    }
}
