using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // Real-issuance columns on NFeDocument, plus the denormalised tenant key.
    //
    // Additive only: every column is added with a default, and the two existing-data columns
    // (EnterpriseId, Model) are backfilled from data already in the database. No column is
    // dropped, retyped or narrowed, so this is safe to run against a populated table.
    //
    // EnterpriseId is a plain indexed column with NO foreign key to Enterprise on purpose. A
    // relationship would create a delete cascade onto fiscal documents, and an authorized NFe
    // must be retained for five years (Ajuste SINIEF 07/2005) — it cannot disappear because a
    // tenant row was removed. The existing Order FK still carries the referential relationship.
    //
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260801130000_AddNFeDocumentFiscalColumns")]
    public partial class AddNFeDocumentFiscalColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Added nullable so the backfill below can run, then tightened to NOT NULL at the end.
            // Adding it NOT NULL with a defaultValue would leave a permanent DEFAULT constraint on
            // the table that the model snapshot does not declare, and the next `migrations add`
            // would emit a spurious AlterColumn to reconcile the difference.
            migrationBuilder.AddColumn<Guid>(
                name: "EnterpriseId",
                table: "NFeDocument",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "Model",
                table: "NFeDocument",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<string>(
                name: "Cstat",
                table: "NFeDocument",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "XMotivo",
                table: "NFeDocument",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReceiptNumber",
                table: "NFeDocument",
                type: "nvarchar(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "TransmittedAtUtc",
                table: "NFeDocument",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AuthorizedAtUtc",
                table: "NFeDocument",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DigestValue",
                table: "NFeDocument",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "NFeDocument",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelRequestedAt",
                table: "NFeDocument",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "NFeDocument",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelCstat",
                table: "NFeDocument",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CancelXMotivo",
                table: "NFeDocument",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "QrCode",
                table: "NFeDocument",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UrlChave",
                table: "NFeDocument",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            // Backfill the denormalised tenant key from the owning order. Rows whose order has
            // no enterprise keep Guid.Empty; tenant scoping treats that as "matches nobody",
            // and NFeService falls back to the Order lookup for them.
            migrationBuilder.Sql("""
                UPDATE d
                   SET d.EnterpriseId = o.EnterpriseId
                  FROM dbo.NFeDocument AS d
                  INNER JOIN dbo.[Order] AS o ON o.Id = d.OrderId
                 WHERE o.EnterpriseId IS NOT NULL;
                """);

            // Anything the join could not resolve becomes Guid.Empty rather than NULL, so the
            // column can go NOT NULL and match the model.
            migrationBuilder.Sql("""
                UPDATE dbo.NFeDocument
                   SET EnterpriseId = '00000000-0000-0000-0000-000000000000'
                 WHERE EnterpriseId IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "EnterpriseId",
                table: "NFeDocument",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            // Type is the API-facing enum (1 = NFe, 2 = NFCe); Model is the SEFAZ document model.
            migrationBuilder.Sql("""
                UPDATE dbo.NFeDocument
                   SET Model = CASE WHEN [Type] = 2 THEN 65 ELSE 55 END
                 WHERE Model = 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_NFeDocument_EnterpriseId",
                table: "NFeDocument",
                column: "EnterpriseId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NFeDocument_EnterpriseId",
                table: "NFeDocument");

            foreach (var column in new[]
                     {
                         "EnterpriseId", "Model", "Cstat", "XMotivo", "ReceiptNumber",
                         "TransmittedAtUtc", "AuthorizedAtUtc", "DigestValue", "AttemptCount",
                         "CancelRequestedAt", "CancelledAtUtc", "CancelCstat", "CancelXMotivo",
                         "QrCode", "UrlChave"
                     })
            {
                migrationBuilder.DropColumn(name: column, table: "NFeDocument");
            }
        }
    }
}
