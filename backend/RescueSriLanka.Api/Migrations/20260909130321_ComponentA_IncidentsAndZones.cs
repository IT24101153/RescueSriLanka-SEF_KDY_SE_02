using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Migrations
{
    /// <inheritdoc />
    public partial class ComponentA_IncidentsAndZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Severity = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    AffectedRadiusMeters = table.Column<int>(type: "integer", nullable: false),
                    District = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AddressText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    EstimatedAffectedPeople = table.Column<int>(type: "integer", nullable: true),
                    AiSeverity = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AiSeverityScore = table.Column<int>(type: "integer", nullable: true),
                    AiConfidence = table.Column<double>(type: "double precision", nullable: true),
                    AiRationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AiAnalysedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SeverityOverriddenBy = table.Column<Guid>(type: "uuid", nullable: true),
                    SeverityOverriddenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReportedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "incident_images",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Caption = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_incident_images_incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "safety_zones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CenterLatitude = table.Column<double>(type: "double precision", nullable: false),
                    CenterLongitude = table.Column<double>(type: "double precision", nullable: false),
                    RadiusMeters = table.Column<int>(type: "integer", nullable: false),
                    District = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Rationale = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SourceIncidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_safety_zones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_safety_zones_incidents_SourceIncidentId",
                        column: x => x.SourceIncidentId,
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_incident_images_IncidentId",
                table: "incident_images",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_District",
                table: "incidents",
                column: "District");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_IsActive",
                table: "incidents",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_Latitude_Longitude",
                table: "incidents",
                columns: new[] { "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_Severity",
                table: "incidents",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_Status",
                table: "incidents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_safety_zones_IsActive",
                table: "safety_zones",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_safety_zones_SourceIncidentId",
                table: "safety_zones",
                column: "SourceIncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_safety_zones_Status",
                table: "safety_zones",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incident_images");

            migrationBuilder.DropTable(
                name: "safety_zones");

            migrationBuilder.DropTable(
                name: "incidents");
        }
    }
}
