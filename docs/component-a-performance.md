# Component A — Performance report

Evidence for the specification's performance requirement (section 12): concurrent requests, response time,
success/failure rate, database response and Agentic AI latency. Every number below was produced by
`tools/perf/load_test.py` (raw output: [perf-results.md](perf-results.md)). Re-run it and replace the figures
if the code changes.

## 1. Method

- **Tool:** `tools/perf/load_test.py` (Python standard library only). It logs in as the seeded Emergency
  Coordinator, then for each endpoint and each concurrency level (1, 10, 50) sends 300 requests (100 for the
  write endpoint) over persistent connections after a 3-request warm-up, and records the HTTP status and
  response time of every request. *Success* means an HTTP 2xx response.
- **Target:** the API built in **Release** mode, running locally on port 5199, against a **local PostgreSQL 16**
  database created for the test and dropped afterwards (migrated and seeded by the API on start-up: 4 accounts,
  10 sample incidents). The deployed Supabase database was **not** used.
- **Switched off for the test:** Gemini (empty key, so the Severity agent runs the rule engine), e-mail, and
  Cloudinary. This keeps the test from spending model quota or sending mail. Open-Meteo (rainfall) was **not**
  switched off, so the agent latency includes a real external HTTP call.
- **Machine:** the load generator and the API ran on the same Apple-silicon Mac (macOS 27), so they compete
  for CPU.

```bash
# 1. a throwaway database and the API against it
createdb rsl_perf
cd backend/RescueSriLanka.Api
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5199 \
ConnectionStrings__DefaultConnection="Host=localhost;Username=$USER;Database=rsl_perf" \
GoogleAi__ApiKey="" Email__Enabled=false \
Cloudinary__CloudName="" Cloudinary__ApiKey="" Cloudinary__ApiSecret="" \
dotnet run -c Release --no-launch-profile
# 2. the test (another terminal)
python3 tools/perf/load_test.py --base http://localhost:5199 \
  --email emergency@rescue.lk --password 'Rescue@123' --out docs/perf-results.md
# 3. clean up
dropdb rsl_perf
```

## 2. Results (1 October 2026)

Run: 2026-10-01 13:29 UTC · target `http://localhost:5199` · macOS-27.0-arm64-arm-64bit-Mach-O · Python 3.14.7

| Endpoint | Concurrency | Requests | Success | Failed | Req/s | Min ms | Mean ms | p50 ms | p95 ms | p99 ms | Max ms |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| GET /health (API + PostgreSQL probe) | 1 | 300 | 100.0% | 0 | 1459 | 0.4 | 0.7 | 0.6 | 0.8 | 1.7 | 14.7 |
| GET /health (API + PostgreSQL probe) | 10 | 300 | 100.0% | 0 | 6669 | 0.5 | 1.4 | 1.0 | 1.8 | 15.9 | 18.5 |
| GET /health (API + PostgreSQL probe) | 50 | 300 | 100.0% | 0 | 4861 | 0.6 | 6.3 | 1.8 | 37.4 | 44.5 | 45.9 |
| GET /api/incidents (page 1, 20 rows) | 1 | 300 | 100.0% | 0 | 410 | 1.4 | 2.4 | 2.3 | 4.7 | 5.8 | 7.5 |
| GET /api/incidents (page 1, 20 rows) | 10 | 300 | 100.0% | 0 | 3688 | 1.3 | 2.6 | 2.4 | 4.6 | 6.5 | 7.2 |
| GET /api/incidents (page 1, 20 rows) | 50 | 300 | 100.0% | 0 | 3979 | 5.6 | 10.9 | 10.9 | 13.4 | 14.6 | 16.2 |
| GET /api/incidents/statistics (aggregates) | 1 | 300 | 100.0% | 0 | 902 | 0.9 | 1.1 | 1.1 | 1.3 | 1.4 | 2.3 |
| GET /api/incidents/statistics (aggregates) | 10 | 300 | 100.0% | 0 | 4193 | 1.2 | 2.3 | 2.2 | 3.3 | 4.0 | 4.2 |
| GET /api/incidents/statistics (aggregates) | 50 | 300 | 100.0% | 0 | 3970 | 5.6 | 11.2 | 11.4 | 14.0 | 14.9 | 15.7 |
| GET /api/safetyzones | 1 | 300 | 100.0% | 0 | 1890 | 0.5 | 0.5 | 0.5 | 0.6 | 0.6 | 1.6 |
| GET /api/safetyzones | 10 | 300 | 100.0% | 0 | 7178 | 0.7 | 1.3 | 1.2 | 2.2 | 3.6 | 5.2 |
| GET /api/safetyzones | 50 | 300 | 100.0% | 0 | 6967 | 1.7 | 5.4 | 5.4 | 7.7 | 10.9 | 12.8 |
| GET /api/incidents/nearby (geo query) | 1 | 300 | 100.0% | 0 | 1301 | 0.6 | 0.7 | 0.7 | 1.0 | 1.1 | 1.8 |
| GET /api/incidents/nearby (geo query) | 10 | 300 | 100.0% | 0 | 4589 | 0.9 | 2.1 | 1.7 | 4.0 | 11.1 | 11.8 |
| GET /api/incidents/nearby (geo query) | 50 | 300 | 100.0% | 0 | 5869 | 1.6 | 7.3 | 7.2 | 10.1 | 12.7 | 13.9 |
| GET /api/agentruns | 1 | 300 | 100.0% | 0 | 1796 | 0.5 | 0.5 | 0.5 | 0.7 | 0.8 | 1.9 |
| GET /api/agentruns | 10 | 300 | 100.0% | 0 | 7095 | 0.6 | 1.3 | 1.3 | 2.1 | 3.3 | 3.5 |
| GET /api/agentruns | 50 | 300 | 100.0% | 0 | 7591 | 0.7 | 4.8 | 4.6 | 7.9 | 8.9 | 9.0 |
| POST /api/incidents (write + queue analysis) | 1 | 100 | 100.0% | 0 | 273 | 2.6 | 3.6 | 3.6 | 4.4 | 4.6 | 4.6 |
| POST /api/incidents (write + queue analysis) | 10 | 100 | 100.0% | 0 | 1395 | 3.6 | 6.9 | 6.2 | 11.9 | 14.1 | 14.1 |
| POST /api/incidents (write + queue analysis) | 50 | 100 | 100.0% | 0 | 1216 | 3.7 | 33.0 | 28.7 | 72.1 | 79.1 | 79.1 |

**Agentic AI latency** (`POST /api/incidents/{id}/analyse`, sequential, model(s) used: rule-engine)

| Measure | Runs | Success | Min ms | Mean ms | p50 ms | Max ms |
|---|--:|--:|--:|--:|--:|--:|
| Endpoint, end to end | 6 | 6/6 | 248 | 384 | 255 | 1040 |
| Agent `DurationMs` (from `agent_runs`) | 6 | 6/6 | 240 | 374 | 242 | 1023 |

## 3. What the results show

- **Success rate: 100 % on every endpoint and concurrency level** (0 failed out of about 5 700 measured requests).
- **Response time:** at 50 concurrent users every read endpoint stayed at or under about 15 ms at p99, except the
  `/health` probe (p99 ≈ 45 ms, a few slow outliers). The write endpoint (`POST /api/incidents`) was the
  slowest, with p95 ≈ 72 ms and p99 ≈ 79 ms at 50 concurrent.
- **Throughput:** reads reached roughly 3 700–7 600 requests/s at 10–50 concurrent users; creating incidents
  about 1 200–1 400/s. Throughput stops growing between 10 and 50 users while latency rises, which is the
  expected saturation point on one machine.
- **Database response:** `/health` runs a real query against PostgreSQL (`DatabaseHealthCheck`) and answered in
  with a median of 0.6 ms single-user and 1.8 ms at 50 concurrent (mean 6.3 ms at 50, p99 ≈ 45 ms). `GET /api/incidents/statistics` (aggregates over the incident
  table) had a median of 1.1 ms single-user and 11 ms at 50 concurrent.
- **Agentic AI latency (rule-engine path, Open-Meteo live):** 6 sequential analyses, 6/6 successful; the
  agent's own `DurationMs` had a median of about 240 ms and a maximum of about 1 s. That time is most likely
  dominated by the rainfall call to Open-Meteo (a real network request; I did not time it separately). The end-to-end endpoint time is
  only about 10 ms more than the agent's own duration.
- **Citizen-facing path:** a report is saved in a few milliseconds because analysis is queued for a background
  worker; a citizen never waits for the agent.

## 4. What these numbers do not show (read before citing them)

- **The model path is not measured.** Gemini was off, so the figures above are for the rule engine. A real
  Gemini call adds network and model time (seconds, with up to 3 retries and a 30 s timeout per call). To
  measure it, start the API with your real key and run
  `python3 tools/perf/load_test.py --agent-only --agent-runs 5 --email … --password …` against a local or
  throwaway database, then add the figures here. Do not estimate them.
- **Local, same-machine, Release build, 10 seeded incidents (about 300 more were created by the write test).**
  It says nothing about the deployed Azure/Supabase environment, which has network latency and a different
  database size.
- **Low data volume.** With a few hundred incidents the geo query (`nearby`, a bounding box then an in-memory
  distance filter) is fast; at national scale it would need a spatial index.
- **Rate limits were respected, not tested.** The `auth` (10/min per IP) and `ai` (10/min per user) policies
  mean login and the manual-analysis endpoint cannot be load tested without tripping them, by design; the agent
  test therefore runs sequentially.
- **The background queue is bounded and serial.** The write test created about 300 incidents faster than the
  worker analyses them (~0.25 s each), so a backlog formed and was still draining when the API was stopped.
  Reports are still saved immediately; their AI proposals arrive later.
- **A hard process kill can leave a run `Running`.** During this test, stopping the API while an analysis was
  in flight was handled (the run was marked `Failed`, "Analysis was cancelled"), but a crash or `kill -9`
  gives no chance to record anything, and no start-up sweep marks such runs. A sweep that fails stale `Running`
  runs on start-up would close this gap.
