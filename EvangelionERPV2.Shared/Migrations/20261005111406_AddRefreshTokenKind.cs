using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "RefreshToken",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "legacy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "RefreshToken");
        }
    }
}
