using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddOpportunityRadar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Opportunity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceRule = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceModel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Hypothesis = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExplainabilityJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfidenceScore = table.Column<double>(type: "float", nullable: false),
                    EstimatedRevenueImpact = table.Column<double>(type: "float", nullable: false),
                    EstimatedMarginImpact = table.Column<double>(type: "float", nullable: false),
                    EstimatedCashImpact = table.Column<double>(type: "float", nullable: false),
                    PriorityScore = table.Column<double>(type: "float", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastEvaluatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Opportunity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Opportunity_Enterprise_EnterpriseId",
                        column: x => x.EnterpriseId,
                        principalTable: "Enterprise",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityRunLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TriggerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalGenerated = table.Column<int>(type: "int", nullable: false),
                    TotalUpdated = table.Column<int>(type: "int", nullable: false),
                    TotalArchived = table.Column<int>(type: "int", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    DetectorStatsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityRunLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpportunityRunLog_Enterprise_EnterpriseId",
                        column: x => x.EnterpriseId,
                        principalTable: "Enterprise",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OpportunityRunLog_User_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RealRevenueImpact = table.Column<double>(type: "float", nullable: true),
                    RealMarginImpact = table.Column<double>(type: "float", nullable: true),
                    RealCashImpact = table.Column<double>(type: "float", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpportunityFeedback_Opportunity_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "Opportunity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OpportunityFeedback_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityRecommendation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionTitle = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ActionDescription = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WhyRecommended = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActionPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PriorityLabel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityRecommendation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpportunityRecommendation_Opportunity_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "Opportunity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpportunitySignal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignalType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SignalKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SignalValue = table.Column<double>(type: "float", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceEntity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceEntityId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunitySignal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpportunitySignal_Opportunity_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "Opportunity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Opportunity_EnterpriseId_Fingerprint",
                table: "Opportunity",
                columns: new[] { "EnterpriseId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Opportunity_EnterpriseId_Type_Status",
                table: "Opportunity",
                columns: new[] { "EnterpriseId", "Type", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Opportunity_Id_CreatedAt_UpdatedAt",
                table: "Opportunity",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Opportunity_Id_CreatedAt_UpdatedAt_IsActive",
                table: "Opportunity",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Opportunity_RunId_Type_IsActive",
                table: "Opportunity",
                columns: new[] { "RunId", "Type", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityFeedback_Id_CreatedAt_UpdatedAt",
                table: "OpportunityFeedback",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityFeedback_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OpportunityFeedback",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityFeedback_OpportunityId_Status_CreatedAt",
                table: "OpportunityFeedback",
                columns: new[] { "OpportunityId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityFeedback_UserId_CreatedAt",
                table: "OpportunityFeedback",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityRecommendation_Id_CreatedAt_UpdatedAt",
                table: "OpportunityRecommendation",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityRecommendation_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OpportunityRecommendation",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityRecommendation_OpportunityId_CreatedAt",
                table: "OpportunityRecommendation",
                columns: new[] { "OpportunityId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityRunLog_EnterpriseId_StartedAt",
                table: "OpportunityRunLog",
                columns: new[] { "EnterpriseId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityRunLog_Id_CreatedAt_UpdatedAt",
                table: "OpportunityRunLog",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityRunLog_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OpportunityRunLog",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityRunLog_RequestedByUserId",
                table: "OpportunityRunLog",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityRunLog_RunId",
                table: "OpportunityRunLog",
                column: "RunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpportunitySignal_Id_CreatedAt_UpdatedAt",
                table: "OpportunitySignal",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunitySignal_Id_CreatedAt_UpdatedAt_IsActive",
                table: "OpportunitySignal",
                columns: new[] { "Id", "CreatedAt", "UpdatedAt", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunitySignal_OpportunityId_SignalType",
                table: "OpportunitySignal",
                columns: new[] { "OpportunityId", "SignalType" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunitySignal_SourceEntity_SourceEntityId",
                table: "OpportunitySignal",
                columns: new[] { "SourceEntity", "SourceEntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpportunityFeedback");

            migrationBuilder.DropTable(
                name: "OpportunityRecommendation");

            migrationBuilder.DropTable(
                name: "OpportunityRunLog");

            migrationBuilder.DropTable(
                name: "OpportunitySignal");

            migrationBuilder.DropTable(
                name: "Opportunity");
        }
    }
}
