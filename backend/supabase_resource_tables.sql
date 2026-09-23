-- Run this in Supabase SQL Editor when the ASP.NET API uses Supabase PostgreSQL.
-- Keep Flutter calling the API; do not expose the Supabase service-role key in the app.

create table if not exists public."HelpRequests" (
    "Id" uuid primary key,
    "RequesterName" varchar(160) not null,
    "ContactNumber" varchar(40) not null,
    "NeedType" varchar(50) not null,
    "Description" varchar(2000) not null,
    "Latitude" numeric null,
    "Longitude" numeric null,
    "Status" varchar(30) not null default 'Pending',
    "CreatedAtUtc" timestamptz not null default now()
);

create index if not exists "IX_HelpRequests_Status_CreatedAtUtc"
    on public."HelpRequests" ("Status", "CreatedAtUtc");

create table if not exists public."Donations" (
    "Id" uuid primary key,
    "DonorName" varchar(160) not null,
    "ContactNumber" varchar(40) not null,
    "DonationType" varchar(80) not null,
    "Quantity" numeric(12, 2) not null check ("Quantity" > 0),
    "Unit" varchar(40) not null,
    "Notes" varchar(1000) null,
    "Status" varchar(30) not null default 'PendingReview',
    "CreatedAtUtc" timestamptz not null default now()
);

create index if not exists "IX_Donations_Status_CreatedAtUtc"
    on public."Donations" ("Status", "CreatedAtUtc");

-- These records are written by the ASP.NET API. Keep public client access disabled
-- unless Supabase Auth and row-level security policies are added.
alter table public."HelpRequests" enable row level security;
alter table public."Donations" enable row level security;

-- Sample data for the Resource Manager dashboard and Flutter app.
-- The fixed IDs and ON CONFLICT clauses make this section safe to rerun.

insert into public."Shelters"
    ("Id", "Name", "Address", "Latitude", "Longitude", "Capacity", "OccupiedCapacity", "IsActive", "CreatedAtUtc")
values
    ('10000000-0000-0000-0000-000000000001', 'Kandy Relief Centre', 'Peradeniya Road, Kandy', 7.2906, 80.6337, 180, 72, true, now()),
    ('10000000-0000-0000-0000-000000000002', 'Colombo Community Shelter', 'Narahenpita, Colombo 05', 6.8941, 79.8760, 240, 118, true, now()),
    ('10000000-0000-0000-0000-000000000003', 'Galle District Safe Centre', 'Wakwella Road, Galle', 6.0329, 80.2168, 120, 34, true, now())
on conflict ("Id") do nothing;

insert into public."MedicalSupplies"
    ("Id", "Name", "Unit", "QuantityOnHand", "LowStockThreshold", "IsActive", "UpdatedAtUtc")
values
    ('20000000-0000-0000-0000-000000000001', 'First aid kits', 'kits', 48, 20, true, now()),
    ('20000000-0000-0000-0000-000000000002', 'Bandages', 'packs', 120, 40, true, now()),
    ('20000000-0000-0000-0000-000000000003', 'Essential medicines', 'boxes', 16, 25, true, now())
on conflict ("Id") do nothing;

insert into public."FoodWaterStocks"
    ("Id", "ItemName", "Unit", "QuantityOnHand", "LowStockThreshold", "IsActive", "UpdatedAtUtc")
values
    ('30000000-0000-0000-0000-000000000001', 'Bottled water', 'litres', 850, 300, true, now()),
    ('30000000-0000-0000-0000-000000000002', 'Rice and dry rations', 'kg', 420, 150, true, now()),
    ('30000000-0000-0000-0000-000000000003', 'Ready-to-eat meals', 'packs', 95, 120, true, now())
on conflict ("Id") do nothing;

insert into public."HelpRequests"
    ("Id", "RequesterName", "ContactNumber", "NeedType", "Description", "Latitude", "Longitude", "Status", "CreatedAtUtc")
values
    ('40000000-0000-0000-0000-000000000001', 'Nimal Perera', '0712345678', 'Food and water', 'Family of four needs drinking water and dry food after flooding.', 7.2906, 80.6337, 'Pending', now() - interval '2 hours'),
    ('40000000-0000-0000-0000-000000000002', 'Ayesha Fernando', '0773456789', 'Medical aid', 'Need first aid supplies for an injured neighbour.', 6.8941, 79.8760, 'InProgress', now() - interval '5 hours')
on conflict ("Id") do nothing;

insert into public."Donations"
    ("Id", "DonorName", "ContactNumber", "DonationType", "Quantity", "Unit", "Notes", "Status", "CreatedAtUtc")
values
    ('50000000-0000-0000-0000-000000000001', 'Lanka Community Group', '0812234567', 'Food and water', 250, 'litres', 'Available for collection in Kandy.', 'PendingReview', now() - interval '1 hour'),
    ('50000000-0000-0000-0000-000000000002', 'Sahan Wijesinghe', '0764567890', 'Medical supplies', 30, 'kits', 'First aid kits, unopened.', 'Accepted', now() - interval '1 day')
on conflict ("Id") do nothing;
