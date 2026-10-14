using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class UseJarIconForCeramica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "StorefrontCategory",
                keyColumn: "Id",
                keyValue: new Guid("e747f721-7afb-d77b-994b-d3d60f3ec8fb"),
                column: "Icon",
                value: "Jar");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "StorefrontCategory",
                keyColumn: "Id",
                keyValue: new Guid("e747f721-7afb-d77b-994b-d3d60f3ec8fb"),
                column: "Icon",
                value: "Vase");
        }
    }
}
