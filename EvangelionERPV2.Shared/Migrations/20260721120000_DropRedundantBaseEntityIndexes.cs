using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // The [Migration] attribute is required for EF to discover this class at
    // Database.Migrate() time — without it the migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260721120000_DropRedundantBaseEntityIndexes")]
    public partial class DropRedundantBaseEntityIndexes : Migration
    {
        // BaseEntity carried [Index(Id, CreatedAt, UpdatedAt)] and a second variant ending
        // in IsActive, so every table inherited two indexes leading with the primary key.
        // A non-clustered index leading with the PK duplicates the PK itself and is never
        // seekable for the queries this app runs; it only costs storage and write amplification
        // on every insert/update. Dropped across all 22 tables.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Boleto_Id_CreatedAt_UpdatedAt",
                table: "Boleto");

            migrationBuilder.DropIndex(
                name: "IX_Boleto_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Boleto");

            migrationBuilder.DropIndex(
                name: "IX_Customer_Id_CreatedAt_UpdatedAt",
                table: "Customer");

            migrationBuilder.DropIndex(
                name: "IX_Customer_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Customer");

            migrationBuilder.DropIndex(
                name: "IX_Email_Id_CreatedAt_UpdatedAt",
                table: "Email");

            migrationBuilder.DropIndex(
                name: "IX_Email_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Email");

            migrationBuilder.DropIndex(
                name: "IX_Employee_Id_CreatedAt_UpdatedAt",
                table: "Employee");

            migrationBuilder.DropIndex(
                name: "IX_Employee_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Employee");

            migrationBuilder.DropIndex(
                name: "IX_Enterprise_Id_CreatedAt_UpdatedAt",
                table: "Enterprise");

            migrationBuilder.DropIndex(
                name: "IX_Enterprise_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Enterprise");

            migrationBuilder.DropIndex(
                name: "IX_EnterprisePayrollSettings_Id_CreatedAt_UpdatedAt",
                table: "EnterprisePayrollSettings");

            migrationBuilder.DropIndex(
                name: "IX_EnterprisePayrollSettings_Id_CreatedAt_UpdatedAt_IsActive",
                table: "EnterprisePayrollSettings");

            migrationBuilder.DropIndex(
                name: "IX_ForecastSimulationLog_Id_CreatedAt_UpdatedAt",
                table: "ForecastSimulationLog");

            migrationBuilder.DropIndex(
                name: "IX_ForecastSimulationLog_Id_CreatedAt_UpdatedAt_IsActive",
                table: "ForecastSimulationLog");

            migrationBuilder.DropIndex(
                name: "IX_NFeDocument_Id_CreatedAt_UpdatedAt",
                table: "NFeDocument");

            migrationBuilder.DropIndex(
                name: "IX_NFeDocument_Id_CreatedAt_UpdatedAt_IsActive",
                table: "NFeDocument");

            migrationBuilder.DropIndex(
                name: "IX_Opportunity_Id_CreatedAt_UpdatedAt",
                table: "Opportunity");

            migrationBuilder.DropIndex(
                name: "IX_Opportunity_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Opportunity");

            migrationBuilder.DropIndex(
                name: "IX_OpportunityFeedback_Id_CreatedAt_UpdatedAt",
                table: "OpportunityFeedback");

            migrationBuilder.DropIndex(
                name: "IX_OpportunityFeedback_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OpportunityFeedback");

            migrationBuilder.DropIndex(
                name: "IX_OpportunityRecommendation_Id_CreatedAt_UpdatedAt",
                table: "OpportunityRecommendation");

            migrationBuilder.DropIndex(
                name: "IX_OpportunityRecommendation_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OpportunityRecommendation");

            migrationBuilder.DropIndex(
                name: "IX_OpportunityRunLog_Id_CreatedAt_UpdatedAt",
                table: "OpportunityRunLog");

            migrationBuilder.DropIndex(
                name: "IX_OpportunityRunLog_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OpportunityRunLog");

            migrationBuilder.DropIndex(
                name: "IX_OpportunitySignal_Id_CreatedAt_UpdatedAt",
                table: "OpportunitySignal");

            migrationBuilder.DropIndex(
                name: "IX_OpportunitySignal_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OpportunitySignal");

            migrationBuilder.DropIndex(
                name: "IX_Order_Id_CreatedAt_UpdatedAt",
                table: "Order");

            migrationBuilder.DropIndex(
                name: "IX_Order_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Order");

            migrationBuilder.DropIndex(
                name: "IX_OrderedProduct_Id_CreatedAt_UpdatedAt",
                table: "OrderedProduct");

            migrationBuilder.DropIndex(
                name: "IX_OrderedProduct_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OrderedProduct");

            migrationBuilder.DropIndex(
                name: "IX_PasswordResetToken_Id_CreatedAt_UpdatedAt",
                table: "PasswordResetToken");

            migrationBuilder.DropIndex(
                name: "IX_PasswordResetToken_Id_CreatedAt_UpdatedAt_IsActive",
                table: "PasswordResetToken");

            migrationBuilder.DropIndex(
                name: "IX_PayableBill_Id_CreatedAt_UpdatedAt",
                table: "PayableBill");

            migrationBuilder.DropIndex(
                name: "IX_PayableBill_Id_CreatedAt_UpdatedAt_IsActive",
                table: "PayableBill");

            migrationBuilder.DropIndex(
                name: "IX_PayableBillProduct_Id_CreatedAt_UpdatedAt",
                table: "PayableBillProduct");

            migrationBuilder.DropIndex(
                name: "IX_PayableBillProduct_Id_CreatedAt_UpdatedAt_IsActive",
                table: "PayableBillProduct");

            migrationBuilder.DropIndex(
                name: "IX_PayrollBillDetail_Id_CreatedAt_UpdatedAt",
                table: "PayrollBillDetail");

            migrationBuilder.DropIndex(
                name: "IX_PayrollBillDetail_Id_CreatedAt_UpdatedAt_IsActive",
                table: "PayrollBillDetail");

            migrationBuilder.DropIndex(
                name: "IX_Product_Id_CreatedAt_UpdatedAt",
                table: "Product");

            migrationBuilder.DropIndex(
                name: "IX_Product_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Product");

            migrationBuilder.DropIndex(
                name: "IX_RefreshToken_Id_CreatedAt_UpdatedAt",
                table: "RefreshToken");

            migrationBuilder.DropIndex(
                name: "IX_RefreshToken_Id_CreatedAt_UpdatedAt_IsActive",
                table: "RefreshToken");

            migrationBuilder.DropIndex(
                name: "IX_User_Id_CreatedAt_UpdatedAt",
                table: "User");

            migrationBuilder.DropIndex(
                name: "IX_User_Id_CreatedAt_UpdatedAt_IsActive",
                table: "User");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Boleto_Id_CreatedAt_UpdatedAt",
                table: "Boleto",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Boleto_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Boleto",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Id_CreatedAt_UpdatedAt",
                table: "Customer",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Customer",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Email_Id_CreatedAt_UpdatedAt",
                table: "Email",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Email_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Email",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Employee_Id_CreatedAt_UpdatedAt",
                table: "Employee",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Employee_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Employee",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Enterprise_Id_CreatedAt_UpdatedAt",
                table: "Enterprise",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Enterprise_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Enterprise",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_EnterprisePayrollSettings_Id_CreatedAt_UpdatedAt",
                table: "EnterprisePayrollSettings",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EnterprisePayrollSettings_Id_CreatedAt_UpdatedAt_IsActive",
                table: "EnterprisePayrollSettings",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ForecastSimulationLog_Id_CreatedAt_UpdatedAt",
                table: "ForecastSimulationLog",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ForecastSimulationLog_Id_CreatedAt_UpdatedAt_IsActive",
                table: "ForecastSimulationLog",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_NFeDocument_Id_CreatedAt_UpdatedAt",
                table: "NFeDocument",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NFeDocument_Id_CreatedAt_UpdatedAt_IsActive",
                table: "NFeDocument",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Opportunity_Id_CreatedAt_UpdatedAt",
                table: "Opportunity",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Opportunity_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Opportunity",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityFeedback_Id_CreatedAt_UpdatedAt",
                table: "OpportunityFeedback",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityFeedback_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OpportunityFeedback",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityRecommendation_Id_CreatedAt_UpdatedAt",
                table: "OpportunityRecommendation",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityRecommendation_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OpportunityRecommendation",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityRunLog_Id_CreatedAt_UpdatedAt",
                table: "OpportunityRunLog",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityRunLog_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OpportunityRunLog",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunitySignal_Id_CreatedAt_UpdatedAt",
                table: "OpportunitySignal",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunitySignal_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OpportunitySignal",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Order_Id_CreatedAt_UpdatedAt",
                table: "Order",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Order_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Order",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderedProduct_Id_CreatedAt_UpdatedAt",
                table: "OrderedProduct",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderedProduct_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OrderedProduct",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetToken_Id_CreatedAt_UpdatedAt",
                table: "PasswordResetToken",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetToken_Id_CreatedAt_UpdatedAt_IsActive",
                table: "PasswordResetToken",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PayableBill_Id_CreatedAt_UpdatedAt",
                table: "PayableBill",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PayableBill_Id_CreatedAt_UpdatedAt_IsActive",
                table: "PayableBill",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PayableBillProduct_Id_CreatedAt_UpdatedAt",
                table: "PayableBillProduct",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PayableBillProduct_Id_CreatedAt_UpdatedAt_IsActive",
                table: "PayableBillProduct",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBillDetail_Id_CreatedAt_UpdatedAt",
                table: "PayrollBillDetail",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBillDetail_Id_CreatedAt_UpdatedAt_IsActive",
                table: "PayrollBillDetail",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Product_Id_CreatedAt_UpdatedAt",
                table: "Product",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Product_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Product",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_Id_CreatedAt_UpdatedAt",
                table: "RefreshToken",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_Id_CreatedAt_UpdatedAt_IsActive",
                table: "RefreshToken",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_User_Id_CreatedAt_UpdatedAt",
                table: "User",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_User_Id_CreatedAt_UpdatedAt_IsActive",
                table: "User",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });
        }
    }
}
