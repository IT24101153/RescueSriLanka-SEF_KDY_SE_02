using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Migrations
{
    /// <inheritdoc />
    public partial class EnhanceComponentDAssignmentSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlanVersion",
                table: "Assignments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "RequiredCapacity",
                table: "Assignments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Assignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "VehicleId",
                table: "Assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_VehicleId",
                table: "Assignments",
                column: "VehicleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assignments_Vehicles_VehicleId",
                table: "Assignments",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assignments_Vehicles_VehicleId",
                table: "Assignments");

            migrationBuilder.DropIndex(
                name: "IX_Assignments_VehicleId",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "PlanVersion",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "RequiredCapacity",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "Assignments");
        }
    }
}
