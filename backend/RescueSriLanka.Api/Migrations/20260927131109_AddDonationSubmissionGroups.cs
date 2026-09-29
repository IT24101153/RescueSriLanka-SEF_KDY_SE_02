using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDonationSubmissionGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubmissionId",
                table: "Donations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Donations_SubmissionId",
                table: "Donations",
                column: "SubmissionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Donations_SubmissionId",
                table: "Donations");

            migrationBuilder.DropColumn(
                name: "SubmissionId",
                table: "Donations");
        }
    }
}
