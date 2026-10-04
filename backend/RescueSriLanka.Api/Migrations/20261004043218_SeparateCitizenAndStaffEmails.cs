using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Migrations.AppDb
{
    /// <inheritdoc />
    public partial class SeparateCitizenAndStaffEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_Email",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email_Citizen",
                table: "users",
                column: "Email",
                unique: true,
                filter: "\"Role\" = 'Citizen'");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email_Staff",
                table: "users",
                column: "Email",
                unique: true,
                filter: "\"Role\" <> 'Citizen'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_Email_Citizen",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_Email_Staff",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);
        }
    }
}
