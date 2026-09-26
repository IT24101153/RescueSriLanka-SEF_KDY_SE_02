using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Migrations
{
    /// <inheritdoc />
    public partial class ComponentBSchemaAlignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequestStatusHistories_HelpRequestId",
                table: "RequestStatusHistories");

            migrationBuilder.DropIndex(
                name: "IX_AgentSteps_AgentWorkflowId",
                table: "AgentSteps");

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequests_RelatedIncidentId",
                table: "HelpRequests",
                column: "RelatedIncidentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HelpRequests_RelatedIncidentId",
                table: "HelpRequests");

            migrationBuilder.CreateIndex(
                name: "IX_RequestStatusHistories_HelpRequestId",
                table: "RequestStatusHistories",
                column: "HelpRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentSteps_AgentWorkflowId",
                table: "AgentSteps",
                column: "AgentWorkflowId");
        }
    }
}
