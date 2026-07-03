using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    public partial class AddEnterpriseScopeToEmailSettings : Migration
    {
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
