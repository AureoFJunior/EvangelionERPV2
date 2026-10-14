using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <summary>
    /// A user can hold a contact number of their own.
    /// </summary>
    /// <remarks>
    /// Non-nullable with an empty default rather than nullable: every other phone column in this
    /// schema is a non-null string, and the screens reading it already treat "" as "not set". A
    /// nullable column here would make this the one phone field callers have to null-check.
    /// No backfill — there is nowhere to derive it from.
    /// </remarks>
    public partial class AddUserPhoneNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "User",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "User");
        }
    }
}
