using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDonatedSupplyInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DonatedSupplies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DonationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DonorName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DonatedSupplies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DonatedSupplies_DonationId",
                table: "DonatedSupplies",
                column: "DonationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DonatedSupplies_IsActive_UpdatedAtUtc",
                table: "DonatedSupplies",
                columns: new[] { "IsActive", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DonatedSupplies");
        }
    }
}
