using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // Structured destinatário address on Customer. enderDest on an NFe 4.00 needs xLgr, nro, xCpl,
    // xBairro, cMun, xMun, UF and CEP as separate fields, and Customer.Adress is one free-text blob
    // that cannot populate any of them. InscricaoEstadual comes along because indIEDest is derived
    // from whether the destinatário has one.
    //
    // Every column is nullable and additive: a customer who never receives an NFe needs none of it,
    // so existing rows stay valid and nothing needs a backfill. Completeness is enforced at
    // issuance, where the error can name the missing field.
    //
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260820100000_AddCustomerFiscalAddress")]
    public partial class AddCustomerFiscalAddress : Migration
    {
        private static readonly (string Name, int MaxLength)[] Columns =
        [
            ("Logradouro", 60),
            ("Numero", 60),
            ("Complemento", 60),
            ("Bairro", 60),
            ("CodigoMunicipioIbge", 7),
            ("Municipio", 60),
            ("Uf", 2),
            ("Cep", 8),
            ("InscricaoEstadual", 14)
        ];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (name, maxLength) in Columns)
            {
                migrationBuilder.AddColumn<string>(
                    name: name,
                    table: "Customer",
                    type: $"nvarchar({maxLength})",
                    maxLength: maxLength,
                    nullable: true);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (name, _) in Columns)
            {
                migrationBuilder.DropColumn(name: name, table: "Customer");
            }
        }
    }
}
