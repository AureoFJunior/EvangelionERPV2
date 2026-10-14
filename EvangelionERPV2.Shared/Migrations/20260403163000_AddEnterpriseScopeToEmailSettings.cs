using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260403163000_AddEnterpriseScopeToEmailSettings")]
    public partial class AddEnterpriseScopeToEmailSettings : Migration
    {
        // PRE-FLIGHT: this migration was undiscoverable (no [Migration], no .Designer.cs) from
        // the day it was written until the attribute above was added, so it has never been
        // recorded in __EFMigrationsHistory anywhere. If an environment acquired
        // [Email].[EnterpriseId] by hand-applied SQL, AddColumn below fails with
        // "Column names in each table must be unique" and aborts the whole run. Check first:
        //
        //   SELECT COL_LENGTH('Email', 'EnterpriseId');   -- must be NULL before applying
        //
        // Also note: 20260414160010 already dropped [IX_Email_EnterpriseId] as a no-op on such
        // databases and will not re-run, so applying this afterwards leaves that index in place
        // even though the model no longer declares it. Cosmetic; drop it by hand if a schema
        // diff needs to be clean.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EnterpriseId",
                table: "Email",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Email_EnterpriseId",
                table: "Email",
                column: "EnterpriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Email_EnterpriseId_IsActive_UserName",
                table: "Email",
                columns: new[] { "EnterpriseId", "IsActive", "UserName" });

            migrationBuilder.AddForeignKey(
                name: "FK_Email_Enterprise_EnterpriseId",
                table: "Email",
                column: "EnterpriseId",
                principalTable: "Enterprise",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Email_Enterprise_EnterpriseId",
                table: "Email");

            migrationBuilder.DropIndex(
                name: "IX_Email_EnterpriseId_IsActive_UserName",
                table: "Email");

            migrationBuilder.DropIndex(
                name: "IX_Email_EnterpriseId",
                table: "Email");

            migrationBuilder.DropColumn(
                name: "EnterpriseId",
                table: "Email");
        }
    }
}
