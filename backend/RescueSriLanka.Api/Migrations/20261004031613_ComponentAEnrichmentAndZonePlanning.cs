using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Migrations.AppDb
{
    /// <inheritdoc />
    public partial class ComponentAEnrichmentAndZonePlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "safety_zones",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceAgentRunId",
                table: "safety_zones",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DuplicateOfIncidentId",
                table: "incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_incidents_DuplicateOfIncidentId",
                table: "incidents",
                column: "DuplicateOfIncidentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_incidents_DuplicateOfIncidentId",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "safety_zones");

            migrationBuilder.DropColumn(
                name: "SourceAgentRunId",
                table: "safety_zones");

            migrationBuilder.DropColumn(
                name: "DuplicateOfIncidentId",
                table: "incidents");
        }
    }
}
