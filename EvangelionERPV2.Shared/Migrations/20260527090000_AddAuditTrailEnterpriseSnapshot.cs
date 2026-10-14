using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    public partial class AddAuditTrailEnterpriseSnapshot : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EnterpriseId",
                table: "AuditTrails",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE [audit]
                SET [EnterpriseId] = [userRecord].[EnterpriseId]
                FROM [AuditTrails] AS [audit]
                INNER JOIN [User] AS [userRecord]
                    ON [audit].[UserId] = [userRecord].[Id]
                WHERE [audit].[EnterpriseId] IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AuditTrails_EnterpriseId_ChangedAt",
                table: "AuditTrails",
                columns: new[] { "EnterpriseId", "ChangedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditTrails_EnterpriseId_ChangedAt",
                table: "AuditTrails");

            migrationBuilder.DropColumn(
                name: "EnterpriseId",
                table: "AuditTrails");
        }
    }
}
