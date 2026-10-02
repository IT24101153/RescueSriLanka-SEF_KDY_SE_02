using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddComponentDAuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "TeamMembers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "TeamMembers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "RescueTeams",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "RescueTeams",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Dispatches",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Dispatches",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Assignments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Assignments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");
            // CURRENT_TIMESTAMP is stable within the migration transaction. For members
            // and vehicles it records the backfill time, NOT a known historical creation time.
            // Existing lifecycle timestamps and RescueTeams.CreatedAt are never changed.
            migrationBuilder.Sql("""
                UPDATE "RescueTeams" SET "UpdatedAt" = "CreatedAt";
                UPDATE "Assignments"
                SET "CreatedAt" = "AssignedAt", "UpdatedAt" = "AssignedAt";

                -- PostgreSQL LEAST/GREATEST ignore NULL inputs. Use the earliest/latest
                -- known lifecycle events; all-null history falls back to migration time.
                UPDATE "Dispatches"
                SET "CreatedAt" = COALESCE(
                    LEAST("ApprovedAt", "DispatchedAt", "EnRouteAt", "OnSceneAt", "ResolvedAt", "CancelledAt"),
                    CURRENT_TIMESTAMP);
                UPDATE "Dispatches"
                SET "UpdatedAt" = COALESCE(
                    GREATEST("CancelledAt", "ResolvedAt", "OnSceneAt", "EnRouteAt", "DispatchedAt", "ApprovedAt"),
                    "CreatedAt");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "TeamMembers");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "TeamMembers");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "RescueTeams");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Dispatches");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Dispatches");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Assignments");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "RescueTeams",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");
        }
    }
}
