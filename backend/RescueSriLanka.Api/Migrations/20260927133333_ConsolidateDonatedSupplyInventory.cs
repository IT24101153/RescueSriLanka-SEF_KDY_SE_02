using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RescueSriLanka.Api.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateDonatedSupplyInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE donation_stock_merge ON COMMIT DROP AS
                WITH parsed AS (
                    SELECT
                        CASE lower(btrim(split_part(d."Name", ':', 1)))
                            WHEN 'food' THEN 'Food'
                            WHEN 'water' THEN 'Water'
                            WHEN 'medical' THEN 'Medical'
                            WHEN 'sanitary products' THEN 'Sanitary products'
                            WHEN 'hygiene items' THEN 'Hygiene items'
                            ELSE 'Other'
                        END AS "Category",
                        CASE
                            WHEN position(':' in d."Name") > 0
                                 AND lower(btrim(split_part(d."Name", ':', 1))) IN
                                     ('food', 'water', 'medical', 'sanitary products', 'hygiene items')
                                THEN btrim(substring(d."Name" from position(':' in d."Name") + 1))
                            ELSE btrim(d."Name")
                        END AS "Name",
                        btrim(d."Unit") AS "Unit",
                        d."QuantityOnHand"
                    FROM "DonatedSupplies" AS d
                    WHERE d."IsActive" AND d."QuantityOnHand" > 0
                )
                  SELECT "Category", MIN("Name") AS "Name", MIN("Unit") AS "Unit",
                      SUM("QuantityOnHand") AS "QuantityOnHand"
                FROM parsed
                  GROUP BY "Category", lower("Name"), lower("Unit");

                UPDATE "ManagedSupplies" AS managed
                SET "QuantityOnHand" = managed."QuantityOnHand" + donated."QuantityOnHand",
                    "UpdatedAtUtc" = CURRENT_TIMESTAMP
                FROM donation_stock_merge AS donated
                WHERE managed."IsActive"
                  AND lower(managed."Category") = lower(donated."Category")
                  AND lower(managed."Name") = lower(donated."Name")
                  AND lower(managed."Unit") = lower(donated."Unit");

                INSERT INTO "ManagedSupplies"
                    ("Id", "Category", "Name", "Unit", "QuantityOnHand", "LowStockThreshold", "IsActive", "UpdatedAtUtc")
                SELECT gen_random_uuid(), donated."Category", donated."Name", donated."Unit",
                       donated."QuantityOnHand", 0, true, CURRENT_TIMESTAMP
                FROM donation_stock_merge AS donated
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "ManagedSupplies" AS managed
                    WHERE managed."IsActive"
                      AND lower(managed."Category") = lower(donated."Category")
                      AND lower(managed."Name") = lower(donated."Name")
                      AND lower(managed."Unit") = lower(donated."Unit"));

                UPDATE "DonatedSupplies" SET "IsActive" = false WHERE "IsActive";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Consolidated quantities cannot be split back to their source donations.
        }
    }
}
