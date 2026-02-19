using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class Language : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "Language",
                table: "User",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "User");
        }
    }
}
