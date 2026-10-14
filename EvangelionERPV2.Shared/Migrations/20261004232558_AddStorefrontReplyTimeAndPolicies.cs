using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddStorefrontReplyTimeAndPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StorefrontPolicies",
                table: "Enterprise",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorefrontReplyTimeHours",
                table: "Enterprise",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorefrontPolicies",
                table: "Enterprise");

            migrationBuilder.DropColumn(
                name: "StorefrontReplyTimeHours",
                table: "Enterprise");
        }
    }
}
