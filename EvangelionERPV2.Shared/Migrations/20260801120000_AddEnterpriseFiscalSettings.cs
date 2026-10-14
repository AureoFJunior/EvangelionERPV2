using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // Per-tenant fiscal profile for real NFe/NFCe issuance, plus the nNF counter table.
    // Both are additive: two new tables, no column added to or altered on an existing one,
    // so this migration is non-destructive and safe to run against a populated database.
    //
    // The EnterpriseFiscalSettings unique index is filtered on [IsActive] = 1 so a soft-deleted
    // row cannot block the create branch of UpsertSettingsAsync. The NFeNumberSequence unique
    // index is deliberately NOT filtered: a replacement counter restarting at 1 would re-issue
    // nNF values already transmitted to SEFAZ.
    //
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260801120000_AddEnterpriseFiscalSettings")]
    public partial class AddEnterpriseFiscalSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnterpriseFiscalSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Cnpj = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: false),
                    InscricaoEstadual = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: false),
                    InscricaoMunicipal = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    RazaoSocial = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    NomeFantasia = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Crt = table.Column<byte>(type: "tinyint", nullable: false),
                    Logradouro = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Numero = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Complemento = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Bairro = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CodigoMunicipioIbge = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    Municipio = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Uf = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    CodigoUfIbge = table.Column<short>(type: "smallint", nullable: false),
                    Cep = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    CodigoPais = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Pais = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Telefone = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: true),
                    Environment = table.Column<byte>(type: "tinyint", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    NFCeEnabled = table.Column<bool>(type: "bit", nullable: false),
                    NFeSeries = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    NFCeSeries = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CscId = table.Column<int>(type: "int", nullable: true),
                    SchemaVersion = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    NFeCancellationWindowMinutes = table.Column<int>(type: "int", nullable: false),
                    NFCeCancellationWindowMinutes = table.Column<int>(type: "int", nullable: false),
                    NFCeCustomerIdentificationThreshold = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CertThumbprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CertSubjectCn = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CertNotAfterUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CertUploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DefaultCfopIntra = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    DefaultCfopInter = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    DefaultCst = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    DefaultCsosn = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    DefaultOrigem = table.Column<byte>(type: "tinyint", nullable: false),
                    DefaultAliquotaIcms = table.Column<decimal>(type: "decimal(5,4)", precision: 18, scale: 2, nullable: false),
                    DefaultCstPis = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    DefaultAliquotaPis = table.Column<decimal>(type: "decimal(5,4)", precision: 18, scale: 2, nullable: false),
                    DefaultCstCofins = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    DefaultAliquotaCofins = table.Column<decimal>(type: "decimal(5,4)", precision: 18, scale: 2, nullable: false),
                    DefaultUnidadeTributavel = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    ContingencyEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ContingencyEnteredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ContingencyReason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnterpriseFiscalSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnterpriseFiscalSettings_Enterprise_EnterpriseId",
                        column: x => x.EnterpriseId,
                        principalTable: "Enterprise",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NFeNumberSequence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Model = table.Column<short>(type: "smallint", nullable: false),
                    Series = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Environment = table.Column<byte>(type: "tinyint", nullable: false),
                    NextNumber = table.Column<int>(type: "int", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NFeNumberSequence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NFeNumberSequence_Enterprise_EnterpriseId",
                        column: x => x.EnterpriseId,
                        principalTable: "Enterprise",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseFiscalSettings_EnterpriseId",
                table: "EnterpriseFiscalSettings",
                column: "EnterpriseId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_NFeNumberSequence_EnterpriseId_Model_Series_Environment",
                table: "NFeNumberSequence",
                columns: new[] { "EnterpriseId", "Model", "Series", "Environment" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnterpriseFiscalSettings");

            migrationBuilder.DropTable(
                name: "NFeNumberSequence");
        }
    }
}
