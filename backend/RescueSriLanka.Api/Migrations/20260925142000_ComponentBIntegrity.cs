using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RescueSriLanka.Api.Data;

#nullable disable

namespace RescueSriLanka.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260925142000_ComponentBIntegrity")]
public sealed class ComponentBIntegrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "HelpRequests" ALTER COLUMN "Type" TYPE character varying(24) USING
                CASE "Type" WHEN 0 THEN 'Water' WHEN 1 THEN 'Food' WHEN 2 THEN 'Medical' WHEN 3 THEN 'Rescue' WHEN 4 THEN 'Shelter' ELSE 'Other' END;
            ALTER TABLE "HelpRequests" ALTER COLUMN "VerificationStatus" TYPE character varying(32) USING
                CASE "VerificationStatus" WHEN 0 THEN 'PendingVerification' WHEN 1 THEN 'Verified' ELSE 'RejectedFake' END;
            ALTER TABLE "TravelAdvisories" ALTER COLUMN "SafetyLevel" TYPE character varying(16) USING
                CASE "SafetyLevel" WHEN 0 THEN 'Safe' WHEN 1 THEN 'Caution' ELSE 'Danger' END;
            ALTER TABLE "HelpRequests" ALTER COLUMN "Description" TYPE character varying(2000);
            ALTER TABLE "HelpRequests" ALTER COLUMN "VerificationNotes" TYPE character varying(1000);
            ALTER TABLE "HelpRequests" ALTER COLUMN "ImageUrl" TYPE character varying(2048);
            ALTER TABLE "RequestStatusHistories" ALTER COLUMN "Notes" TYPE character varying(1000);
            ALTER TABLE "TravelAdvisories" ALTER COLUMN "AreaName" TYPE character varying(200);
            ALTER TABLE "TravelAdvisories" ALTER COLUMN "Reason" TYPE character varying(1000);
            """);

        migrationBuilder.AddColumn<DateTime>(name: "UpdatedAt", table: "TravelAdvisories",
            type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP");

        migrationBuilder.AddForeignKey(name: "FK_HelpRequests_users_CitizenId", table: "HelpRequests",
            column: "CitizenId", principalTable: "users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_HelpRequests_incidents_RelatedIncidentId", table: "HelpRequests",
            column: "RelatedIncidentId", principalTable: "incidents", principalColumn: "Id", onDelete: ReferentialAction.SetNull);

        migrationBuilder.CreateIndex(name: "IX_HelpRequests_CitizenId_CreatedAt", table: "HelpRequests", columns: new[] { "CitizenId", "CreatedAt" });
        migrationBuilder.CreateIndex(name: "IX_HelpRequests_Status_UrgencyScore", table: "HelpRequests", columns: new[] { "Status", "UrgencyScore" });
        migrationBuilder.CreateIndex(name: "IX_HelpRequests_VerificationStatus", table: "HelpRequests", column: "VerificationStatus");
        migrationBuilder.CreateIndex(name: "IX_RequestStatusHistories_HelpRequestId_ChangedAt", table: "RequestStatusHistories", columns: new[] { "HelpRequestId", "ChangedAt" });
        migrationBuilder.CreateIndex(name: "IX_TravelAdvisories_ExpiresAt", table: "TravelAdvisories", column: "ExpiresAt");
        migrationBuilder.CreateIndex(name: "IX_AgentWorkflows_ObjectiveType_ObjectiveId_Status", table: "AgentWorkflows", columns: new[] { "ObjectiveType", "ObjectiveId", "Status" });
        migrationBuilder.CreateIndex(name: "IX_AgentSteps_AgentWorkflowId_StepNumber", table: "AgentSteps", columns: new[] { "AgentWorkflowId", "StepNumber" }, unique: true);

        migrationBuilder.AddCheckConstraint(name: "CK_HelpRequests_UrgencyScore", table: "HelpRequests", sql: "\"UrgencyScore\" >= 0 AND \"UrgencyScore\" <= 100");
        migrationBuilder.AddCheckConstraint(name: "CK_HelpRequests_Latitude", table: "HelpRequests", sql: "\"Latitude\" >= -90 AND \"Latitude\" <= 90");
        migrationBuilder.AddCheckConstraint(name: "CK_HelpRequests_Longitude", table: "HelpRequests", sql: "\"Longitude\" >= -180 AND \"Longitude\" <= 180");
        migrationBuilder.AddCheckConstraint(name: "CK_TravelAdvisories_RadiusMeters", table: "TravelAdvisories", sql: "\"RadiusMeters\" > 0 AND \"RadiusMeters\" <= 100000");
        migrationBuilder.AddCheckConstraint(name: "CK_TravelAdvisories_Latitude", table: "TravelAdvisories", sql: "\"Latitude\" >= -90 AND \"Latitude\" <= 90");
        migrationBuilder.AddCheckConstraint(name: "CK_TravelAdvisories_Longitude", table: "TravelAdvisories", sql: "\"Longitude\" >= -180 AND \"Longitude\" <= 180");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_HelpRequests_UrgencyScore", "HelpRequests");
        migrationBuilder.DropCheckConstraint("CK_HelpRequests_Latitude", "HelpRequests");
        migrationBuilder.DropCheckConstraint("CK_HelpRequests_Longitude", "HelpRequests");
        migrationBuilder.DropCheckConstraint("CK_TravelAdvisories_RadiusMeters", "TravelAdvisories");
        migrationBuilder.DropCheckConstraint("CK_TravelAdvisories_Latitude", "TravelAdvisories");
        migrationBuilder.DropCheckConstraint("CK_TravelAdvisories_Longitude", "TravelAdvisories");
        migrationBuilder.DropForeignKey("FK_HelpRequests_users_CitizenId", "HelpRequests");
        migrationBuilder.DropForeignKey("FK_HelpRequests_incidents_RelatedIncidentId", "HelpRequests");
        migrationBuilder.DropIndex("IX_HelpRequests_CitizenId_CreatedAt", "HelpRequests");
        migrationBuilder.DropIndex("IX_HelpRequests_Status_UrgencyScore", "HelpRequests");
        migrationBuilder.DropIndex("IX_HelpRequests_VerificationStatus", "HelpRequests");
        migrationBuilder.DropIndex("IX_RequestStatusHistories_HelpRequestId_ChangedAt", "RequestStatusHistories");
        migrationBuilder.DropIndex("IX_TravelAdvisories_ExpiresAt", "TravelAdvisories");
        migrationBuilder.DropIndex("IX_AgentWorkflows_ObjectiveType_ObjectiveId_Status", "AgentWorkflows");
        migrationBuilder.DropIndex("IX_AgentSteps_AgentWorkflowId_StepNumber", "AgentSteps");
        migrationBuilder.DropColumn("UpdatedAt", "TravelAdvisories");
        migrationBuilder.Sql("""
            ALTER TABLE "HelpRequests" ALTER COLUMN "Type" TYPE integer USING
                CASE "Type" WHEN 'Water' THEN 0 WHEN 'Food' THEN 1 WHEN 'Medical' THEN 2 WHEN 'Rescue' THEN 3 WHEN 'Shelter' THEN 4 ELSE 5 END;
            ALTER TABLE "HelpRequests" ALTER COLUMN "VerificationStatus" TYPE integer USING
                CASE "VerificationStatus" WHEN 'PendingVerification' THEN 0 WHEN 'Verified' THEN 1 ELSE 2 END;
            ALTER TABLE "TravelAdvisories" ALTER COLUMN "SafetyLevel" TYPE integer USING
                CASE "SafetyLevel" WHEN 'Safe' THEN 0 WHEN 'Caution' THEN 1 ELSE 2 END;
            ALTER TABLE "HelpRequests" ALTER COLUMN "Description" TYPE text;
            ALTER TABLE "HelpRequests" ALTER COLUMN "VerificationNotes" TYPE text;
            ALTER TABLE "HelpRequests" ALTER COLUMN "ImageUrl" TYPE text;
            ALTER TABLE "RequestStatusHistories" ALTER COLUMN "Notes" TYPE text;
            ALTER TABLE "TravelAdvisories" ALTER COLUMN "AreaName" TYPE text;
            ALTER TABLE "TravelAdvisories" ALTER COLUMN "Reason" TYPE text;
            """);
    }
}
