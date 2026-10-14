using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <summary>
    /// A login can say it belongs to a process rather than to a person.
    /// </summary>
    /// <remarks>
    /// No backfill, unlike `AddOrderPaymentMethod` before it, and the difference is that this one
    /// cannot be derived. Which accounts are robots is not recoverable from the rows: the order
    /// worker's login is Admin like a director is, and its name is whatever that deployment's
    /// secret says. Guessing at an email pattern here would silently hide a real seller in some
    /// tenant, and the default — everybody is a person — fails in the safe direction instead.
    /// Mark them where you know them, one UPDATE per deployment, or from the users screen.
    /// </remarks>
    public partial class AddUserIsServiceAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsServiceAccount",
                table: "User",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsServiceAccount",
                table: "User");
        }
    }
}
