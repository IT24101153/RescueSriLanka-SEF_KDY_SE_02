using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedSupplyCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManagedSupplies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    LowStockThreshold = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedSupplies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedSupplies_IsActive_Category_Name",
                table: "ManagedSupplies",
                columns: new[] { "IsActive", "Category", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManagedSupplies");
        }
    }
}
