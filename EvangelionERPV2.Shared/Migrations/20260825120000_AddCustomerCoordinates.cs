using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    // Mapa de clientes. Two nullable coordinate columns and the index the radius search filters on.
    //
    // decimal(9,6), not the global money precision: at decimal(18,2) a latitude of -23.5505 becomes
    // -23.55, roughly half a kilometre, which is enough to put a customer on the wrong side of a
    // radius. Six decimal places is about 11 cm.
    //
    // The index covers the bounding-box comparison the nearby query runs — a Haversine cannot use
    // an index, so the query filters a box in SQL and cuts the circle in memory. Filtered to rows
    // that actually have coordinates: a tenant that has never run the backfill should not carry an
    // index full of nulls.
    //
    // The [Migration] attribute is required for EF to discover this class — without it the
    // migration is silently skipped.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260825120000_AddCustomerCoordinates")]
    public partial class AddCustomerCoordinates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "Customer",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "Customer",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customer_EnterpriseId_Latitude_Longitude",
                table: "Customer",
                columns: new[] { "EnterpriseId", "Latitude", "Longitude" },
                filter: "[Latitude] IS NOT NULL AND [Longitude] IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Customer_EnterpriseId_Latitude_Longitude", table: "Customer");
            migrationBuilder.DropColumn(name: "Longitude", table: "Customer");
            migrationBuilder.DropColumn(name: "Latitude", table: "Customer");
        }
    }
}
