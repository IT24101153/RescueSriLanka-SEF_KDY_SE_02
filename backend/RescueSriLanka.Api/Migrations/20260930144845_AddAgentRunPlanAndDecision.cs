using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentRunPlanAndDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Decision",
                table: "agent_runs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<string>(
                name: "DecisionNote",
                table: "agent_runs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModelAttempts",
                table: "agent_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PlanJson",
                table: "agent_runs",
                type: "text",
                nullable: true);

            // Runs decided before this column existed: "Approved" was the only
            // record, and a rejected run carried an ApprovedAt with Approved false.
            migrationBuilder.Sql(
                "UPDATE agent_runs SET \"Decision\" = 'Approved' WHERE \"Approved\" = TRUE;");
            migrationBuilder.Sql(
                "UPDATE agent_runs SET \"Decision\" = 'Rejected' WHERE \"Approved\" = FALSE AND \"ApprovedAt\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Decision",
                table: "agent_runs");

            migrationBuilder.DropColumn(
                name: "DecisionNote",
                table: "agent_runs");

            migrationBuilder.DropColumn(
                name: "ModelAttempts",
                table: "agent_runs");

            migrationBuilder.DropColumn(
                name: "PlanJson",
                table: "agent_runs");
        }
    }
}
