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
