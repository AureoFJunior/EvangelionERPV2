using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <summary>
    /// natOp becomes tenant configuration instead of a constant in the XML builder.
    /// </summary>
    /// <remarks>
    /// The backfill is hand-corrected, like `AddNFeContingency` and `AddNFCeContingencySeries`
    /// before it: EF generates <c>defaultValue: ""</c>, and every note ever issued by this system
    /// carried "VENDA DE MERCADORIA". An existing profile must keep saying what its past notes said,
    /// and an empty natOp is refused by the 4.00 schema.
    /// </remarks>
    public partial class AddFiscalNatOp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultNatOp",
                table: "EnterpriseFiscalSettings",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "VENDA DE MERCADORIA");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultNatOp",
                table: "EnterpriseFiscalSettings");
        }
    }
}
