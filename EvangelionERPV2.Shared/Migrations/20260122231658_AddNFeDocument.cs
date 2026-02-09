using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddNFeDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NFeDocument",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AccessKey = table.Column<string>(type: "nvarchar(44)", maxLength: 44, nullable: false),
                    Series = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Protocol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalValue = table.Column<double>(type: "float", nullable: false),
                    XmlContent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CancelReason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CancelProtocol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NFeDocument", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NFeDocument_Order_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Order",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NFeDocument_AccessKey",
                table: "NFeDocument",
                column: "AccessKey");

            migrationBuilder.CreateIndex(
                name: "IX_NFeDocument_CreatedAt_UpdatedAt_IsActive_OrderId",
                table: "NFeDocument",
                columns: new[] { "CreatedAt", "UpdatedAt", "IsActive", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_NFeDocument_Id_CreatedAt_UpdatedAt",
                table: "NFeDocument",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NFeDocument_Id_CreatedAt_UpdatedAt_IsActive",
                table: "NFeDocument",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_NFeDocument_OrderId",
                table: "NFeDocument",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NFeDocument");
        }
    }
}
