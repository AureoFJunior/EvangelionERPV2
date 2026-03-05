using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddPayablesAndForecastLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ForecastSimulationLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExecutedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScenarioName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HorizonInDays = table.Column<int>(type: "int", nullable: false),
                    FinalProjectedBalance = table.Column<double>(type: "float", nullable: false),
                    InputsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForecastSimulationLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayableBill",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Amount = table.Column<double>(type: "float", nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayableBill", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayableBill_Enterprise_EnterpriseId",
                        column: x => x.EnterpriseId,
                        principalTable: "Enterprise",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ForecastSimulationLog_EnterpriseId_ExecutedAt",
                table: "ForecastSimulationLog",
                columns: new[] { "EnterpriseId", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ForecastSimulationLog_Id_CreatedAt_UpdatedAt",
                table: "ForecastSimulationLog",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ForecastSimulationLog_Id_CreatedAt_UpdatedAt_IsActive",
                table: "ForecastSimulationLog",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PayableBill_EnterpriseId_DueDate",
                table: "PayableBill",
                columns: new[] { "EnterpriseId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PayableBill_Id_CreatedAt_UpdatedAt",
                table: "PayableBill",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PayableBill_Id_CreatedAt_UpdatedAt_IsActive",
                table: "PayableBill",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ForecastSimulationLog");

            migrationBuilder.DropTable(
                name: "PayableBill");
        }
    }
}
