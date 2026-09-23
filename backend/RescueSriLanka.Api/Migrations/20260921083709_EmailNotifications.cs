using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmailNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "District",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Hand-edited from the generated `false`: EF back-fills existing rows
            // with the CLR default, which would leave every account that already
            // exists silently opted out of a warning system built to reach them.
            // The model's default is true, and accounts created before this
            // migration should match it.
            migrationBuilder.AddColumn<bool>(
                name: "EmailNotificationsEnabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DistrictWarningSentAt",
                table: "incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_District",
                table: "users",
                column: "District");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_District",
                table: "users");

            migrationBuilder.DropColumn(
                name: "District",
                table: "users");

            migrationBuilder.DropColumn(
                name: "EmailNotificationsEnabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "DistrictWarningSentAt",
                table: "incidents");
        }
    }
}
