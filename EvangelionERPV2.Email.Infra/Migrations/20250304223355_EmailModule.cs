using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.EmailModule.Infra.Migrations
{
    /// <inheritdoc />
    public partial class EmailModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Email",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Password = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Email", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Email_CreatedAt_UpdatedAt_IsActive_UserName",
                table: "Email",
                columns: new[] { "CreatedAt", "UpdatedAt", "IsActive", "UserName" });

            migrationBuilder.CreateIndex(
                name: "IX_Email_Id_CreatedAt_UpdatedAt",
                table: "Email",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Email_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Email",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Email_UserName",
                table: "Email",
                column: "UserName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Email");
        }
    }
}
