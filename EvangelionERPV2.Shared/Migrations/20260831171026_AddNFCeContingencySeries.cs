using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddNFCeContingencySeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NFCeContingencySeries",
                table: "EnterpriseFiscalSettings",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,

                // "9", not "". An empty series fails the int.TryParse in
                // AssignNumberAndAccessKeyAsync, so backfilling "" would arm every existing tenant
                // with a contingency path that throws the first time a shop actually needs it.
                defaultValue: "9");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NFCeContingencySeries",
                table: "EnterpriseFiscalSettings");
        }
    }
}
