# Rescue Sri Lanka

A disaster-response platform for Sri Lanka. It connects people affected by a disaster, relief donors, emergency coordinators and rescue teams. AI agents help triage incidents, plan safe routes, allocate resources and dispatch teams.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![React 19](https://img.shields.io/badge/React-19-61DAFB)
![Flutter](https://img.shields.io/badge/Flutter-3.x-02569B)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1)

## Overview

The system has three parts that share one backend API:

| Part | Folder | Stack | Used by |
|------|--------|-------|---------|
| REST API | [backend/RescueSriLanka.Api](backend/RescueSriLanka.Api) | ASP.NET Core (.NET 10), EF Core, PostgreSQL | Web and mobile clients |
| Web app | [frontend](frontend) | React 19, TypeScript, Vite, Leaflet | Coordinators, staff |
| Mobile app | [mobile](mobile) | Flutter, flutter_map, Firebase Messaging | Citizens, field users |

### Feature components

The work is split into four components. Each exists in the backend (`Features/ComponentX`), the web app (`src/components/componentX`) and the mobile app (`lib/features/component_x`).

| Component | Area | AI agents |
|-----------|------|-----------|
| **A** | Incident reporting, safety zones, notifications | Incident analysis, incident enrichment, zone planning |
| **B** | Help requests, travel advisories | Planner agent |
| **C** | Resource requests and donations, medical supplies | Resource allocation agent |
| **D** | Rescue teams, assignments, dispatch | Orchestration, safety validation |

### Other capabilities

- JWT authentication and role-based access
- Google Gemini integration for the agents
- Push notifications through Firebase Cloud Messaging
- Email notifications over SMTP, with a logging fallback
- Photo uploads to Cloudinary
- Interactive maps (Leaflet on the web, flutter_map on mobile)
- Swagger / OpenAPI docs for the API

## Repository layout

```
.
├── backend/
│   ├── RescueSriLanka.Api/        # ASP.NET Core Web API (Controllers, Features/A-D, Services, Migrations)
│   └── RescueSriLanka.Api.Tests/  # Backend tests
├── frontend/                      # React + Vite web app
├── mobile/                        # Flutter app (Android, iOS, web, desktop targets)
├── tools/
│   ├── ensure-backend.ps1         # Starts the local API if it isn't running
│   └── perf/load_test.py          # Load and latency test for the API
├── .github/workflows/             # CI and deployment
└── RescueSriLanka.slnx            # .NET solution
```

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/) and npm
- [Flutter SDK](https://docs.flutter.dev/get-started/install) (Dart `^3.13`), only for the mobile app
- [PostgreSQL 16](https://www.postgresql.org/) (a local instance or a hosted one such as Supabase)

## Getting started

### 1. Backend API

```bash
cd backend/RescueSriLanka.Api

# Point the API at your database. Use user-secrets or an environment variable, not appsettings.json.
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=rescue;Username=postgres;Password=postgres"

dotnet run --launch-profile http
```

The API listens on `http://localhost:5093`. Set `Database__MigrateOnStartup=true` to apply EF Core migrations on startup, or run them yourself:

```bash
dotnet ef database update
```

### 2. Web app

```bash
cd frontend
npm install
npm run dev
```

The app talks to `http://localhost:5093` by default.

### 3. Mobile app

```bash
cd mobile
flutter pub get
flutter run
```

The Android emulator reaches the local API at `http://10.0.2.2:5093`. For Android Studio setup and troubleshooting, see [mobile/README.md](mobile/README.md).

## Configuration

The API reads standard ASP.NET Core configuration. Environment variables use `__` in place of `:`, for example `GoogleAi__ApiKey`.

| Key | Purpose |
|-----|---------|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `Jwt:*` | Token signing settings |
| `Cors:AllowedOrigins` | Origins allowed to call the API |
| `GoogleAi:ApiKey`, `GoogleAi:Model` | Gemini access for the AI agents |
| `Email:*` | SMTP settings, `Email:Enabled` to turn mail on or off |
| `Cloudinary:*` | Photo storage |
| `Swagger:Enabled` | Expose Swagger UI |
| `Database:MigrateOnStartup` | Apply migrations when the API starts |
| `Database:SeedPlaceholderStaff`, `Database:SeedSampleStock`, `Database:SeedComponentBDemo`, `ComponentD:SeedDemoData`, `SeedSampleIncidents` | Seed demo data |

Frontend variables (in `frontend/.env`):

| Variable | Purpose |
|----------|---------|
| `VITE_API_URL` | Base URL of the API (default `http://localhost:5093`) |
| `VITE_MAPBOX_TOKEN`, `VITE_MAPBOX_STYLE` | Optional Mapbox map tiles |

Never commit secrets. `.env` files are git-ignored. Keep Supabase service-role keys on the server only, never in the mobile or web clients.

## Testing

```bash
# Backend (PostgreSQL integration tests read TEST_POSTGRES)
dotnet test RescueSriLanka.slnx

# Web app
cd frontend && npm run lint && npm test && npm run build

# Mobile app
cd mobile && flutter analyze && flutter test
```

To measure API performance, run [tools/perf/load_test.py](tools/perf/load_test.py) against a local or throwaway API only. It creates incidents and runs the agents.

## Continuous integration and deployment

- [ci.yml](.github/workflows/ci.yml) builds and tests all three parts on every push and pull request. It runs the backend tests against a PostgreSQL service container.
- [main_rescuesl-api.yml](.github/workflows/main_rescuesl-api.yml) deploys the API.
- The API includes a [Dockerfile](backend/RescueSriLanka.Api/Dockerfile) for container hosts. The web app is configured for Vercel through [vercel.json](frontend/vercel.json).

## Contributing

1. Create a branch from `main`.
2. Make your change and run the tests for the part you touched.
3. Open a pull request. CI must pass before merging.

## Team

SEF KDY SE 02.
