using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Data.Migrations;

[Migration("20260921180000_RepairHelpRequestSchema")]
public partial class RepairHelpRequestSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "HelpRequests"
                ADD COLUMN IF NOT EXISTS "RequesterName" varchar(160) NOT NULL DEFAULT '';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"HelpRequests\" DROP COLUMN IF EXISTS \"RequesterName\";");
    }
}
