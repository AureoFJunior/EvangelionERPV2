using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // Per-item fiscal data on Product. Every column is nullable and additive: null means "fall
    // back to EnterpriseFiscalSettings.Default*", so existing rows stay valid and nothing needs a
    // backfill. Product.UnitOfMeasure is untouched — it remains the commercial unit (uCom).
    //
    // The four rate columns are decimal(5,4), not the conventional decimal(18,2): a PIS rate of
    // 0.0765 must not round to 0.08.
    //
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260801140000_AddProductFiscalColumns")]
    public partial class AddProductFiscalColumns : Migration
    {
        private static readonly (string Name, int MaxLength)[] StringColumns =
        [
            ("Ncm", 8),
            ("Cest", 7),
            ("Cean", 14),
            ("CfopIntra", 4),
            ("CfopInter", 4),
            ("UnidadeTributavel", 6),
            ("Cst", 3),
            ("Csosn", 3),
            ("CstPis", 2),
            ("CstCofins", 2),
            ("CodigoBeneficioFiscal", 10)
        ];

        private static readonly string[] RateColumns =
        [
            "AliquotaIcms", "AliquotaPis", "AliquotaCofins"
        ];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (name, maxLength) in StringColumns)
            {
                migrationBuilder.AddColumn<string>(
                    name: name,
                    table: "Product",
                    type: $"nvarchar({maxLength})",
                    maxLength: maxLength,
                    nullable: true);
            }

            migrationBuilder.AddColumn<byte>(
                name: "Origem",
                table: "Product",
                type: "tinyint",
                nullable: true);

            foreach (var column in RateColumns)
            {
                migrationBuilder.AddColumn<decimal>(
                    name: column,
                    table: "Product",
                    type: "decimal(5,4)",
                    nullable: true);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (name, _) in StringColumns)
            {
                migrationBuilder.DropColumn(name: name, table: "Product");
            }

            migrationBuilder.DropColumn(name: "Origem", table: "Product");

            foreach (var column in RateColumns)
            {
                migrationBuilder.DropColumn(name: column, table: "Product");
            }
        }
    }
}
