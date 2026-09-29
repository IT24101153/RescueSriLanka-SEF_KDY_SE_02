using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Migrations.AppDb
{
    /// <inheritdoc />
    public partial class RemoveComponentCShelters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"Shelters\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
