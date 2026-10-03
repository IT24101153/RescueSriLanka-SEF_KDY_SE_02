using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Migrations.AppDb
{
    /// <inheritdoc />
    public partial class AddEstimatedPeopleCountToHelpRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EstimatedPeopleCount",
                table: "HelpRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_HelpRequests_EstimatedPeopleCount",
                table: "HelpRequests",
                sql: "\"EstimatedPeopleCount\" IS NULL OR \"EstimatedPeopleCount\" >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_HelpRequests_EstimatedPeopleCount",
                table: "HelpRequests");

            migrationBuilder.DropColumn(
                name: "EstimatedPeopleCount",
                table: "HelpRequests");
        }
    }
}
