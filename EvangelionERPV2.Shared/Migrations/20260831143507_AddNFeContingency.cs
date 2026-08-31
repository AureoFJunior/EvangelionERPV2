using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddNFeContingency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContingencyEnteredAt",
                table: "NFeDocument",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContingencyJustification",
                table: "NFeDocument",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            // 1, not 0. tpEmis has no zero — the valid range is 1..9 — and every note written
            // before this column existed was issued normally, which is exactly what 1 means.
            // Backfilling 0 would leave the whole history claiming an emission type that does
            // not exist.
            migrationBuilder.AddColumn<byte>(
                name: "TipoEmissao",
                table: "NFeDocument",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContingencyEnteredAt",
                table: "NFeDocument");

            migrationBuilder.DropColumn(
                name: "ContingencyJustification",
                table: "NFeDocument");

            migrationBuilder.DropColumn(
                name: "TipoEmissao",
                table: "NFeDocument");
        }
    }
}
