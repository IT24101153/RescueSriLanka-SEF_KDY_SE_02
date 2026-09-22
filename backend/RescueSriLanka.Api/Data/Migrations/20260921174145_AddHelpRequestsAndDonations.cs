using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHelpRequestsAndDonations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "Donations" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "DonorName" varchar(160) NOT NULL,
                    "ContactNumber" varchar(40) NOT NULL,
                    "DonationType" varchar(80) NOT NULL,
                    "Quantity" numeric(12,2) NOT NULL,
                    "Unit" varchar(40) NOT NULL,
                    "Notes" varchar(1000),
                    "Status" varchar(30) NOT NULL DEFAULT 'PendingReview',
                    "CreatedAtUtc" timestamptz NOT NULL DEFAULT now()
                );
                CREATE TABLE IF NOT EXISTS "HelpRequests" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "RequesterName" varchar(160) NOT NULL,
                    "ContactNumber" varchar(40) NOT NULL,
                    "NeedType" varchar(50) NOT NULL,
                    "Description" varchar(2000) NOT NULL,
                    "Latitude" numeric,
                    "Longitude" numeric,
                    "Status" varchar(30) NOT NULL DEFAULT 'Pending',
                    "CreatedAtUtc" timestamptz NOT NULL DEFAULT now()
                );
                ALTER TABLE "HelpRequests"
                    ADD COLUMN IF NOT EXISTS "ContactNumber" varchar(40) NOT NULL DEFAULT '',
                    ADD COLUMN IF NOT EXISTS "NeedType" varchar(50) NOT NULL DEFAULT 'Other',
                    ADD COLUMN IF NOT EXISTS "Description" varchar(2000) NOT NULL DEFAULT '',
                    ADD COLUMN IF NOT EXISTS "Latitude" numeric,
                    ADD COLUMN IF NOT EXISTS "Longitude" numeric,
                    ADD COLUMN IF NOT EXISTS "Status" varchar(30) NOT NULL DEFAULT 'Pending',
                    ADD COLUMN IF NOT EXISTS "CreatedAtUtc" timestamptz NOT NULL DEFAULT now();
                CREATE INDEX IF NOT EXISTS "IX_Donations_Status_CreatedAtUtc"
                    ON "Donations" ("Status", "CreatedAtUtc");
                CREATE INDEX IF NOT EXISTS "IX_HelpRequests_Status_CreatedAtUtc"
                    ON "HelpRequests" ("Status", "CreatedAtUtc");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Donations");

            migrationBuilder.DropTable(
                name: "HelpRequests");
        }
    }
}
