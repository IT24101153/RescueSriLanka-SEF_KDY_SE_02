#!/usr/bin/env python3
"""
Load / latency test for the RescueSriLanka API (Component A endpoints).

Standard library only, so it runs anywhere python3 does:

    python3 tools/perf/load_test.py --base http://localhost:5199 \
        --email emergency@rescue.lk --password 'Rescue@123' --out docs/perf-results.md

POINT IT AT A LOCAL / THROWAWAY API ONLY. It creates incidents and runs the
agent. Start that API with Gemini and email switched off (see
docs/component-a-performance.md) so the test neither spends model quota nor
sends mail.

Measures, per endpoint and concurrency level: requests, success/failure rate
(success = HTTP 2xx), throughput, and response time min / mean / p50 / p95 /
p99 / max. The agent section measures POST /api/incidents/{id}/analyse end to
end and the agent's own DurationMs from /api/agentruns.
"""
import argparse
import http.client
import json
import platform
import statistics
import sys
import threading
import time
import urllib.parse
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone

_local = threading.local()


def _conn(base):
    if getattr(_local, "conn", None) is None:
        u = urllib.parse.urlparse(base)
        _local.conn = http.client.HTTPConnection(u.hostname, u.port, timeout=60)
    return _local.conn


def request(base, method, path, token=None, body=None):
    """One request; returns (status, seconds, parsed_body_or_None)."""
    headers = {"Accept": "application/json"}
    data = None
    if token:
        headers["Authorization"] = f"Bearer {token}"
    if body is not None:
        data = json.dumps(body)
        headers["Content-Type"] = "application/json"
    start = time.perf_counter()
    try:
        conn = _conn(base)
        conn.request(method, path, body=data, headers=headers)
        resp = conn.getresponse()
        raw = resp.read()
        status = resp.status
    except Exception:
        _local.conn = None  # drop a broken connection
        return 0, time.perf_counter() - start, None
    elapsed = time.perf_counter() - start
    try:
        parsed = json.loads(raw) if raw else None
    except ValueError:
        parsed = None
    return status, elapsed, parsed


def pct(sorted_ms, p):
    if not sorted_ms:
        return 0.0
    k = max(0, min(len(sorted_ms) - 1, int(round(p / 100 * len(sorted_ms) + 0.5)) - 1))
    return sorted_ms[k]


def run_scenario(base, name, method, path_fn, token, body_fn, total, concurrency):
    results = []
    lock = threading.Lock()

    def one(i):
        status, secs, _ = request(base, method, path_fn(i), token, body_fn(i) if body_fn else None)
        with lock:
            results.append((status, secs * 1000))

    # warm-up, not counted: JIT, connection pool, EF model
    for i in range(3):
        request(base, method, path_fn(i), token, body_fn(i) if body_fn else None)

    t0 = time.perf_counter()
    with ThreadPoolExecutor(max_workers=concurrency) as pool:
        list(pool.map(one, range(total)))
    wall = time.perf_counter() - t0

    times = sorted(ms for _, ms in results)
    ok = sum(1 for s, _ in results if 200 <= s < 300)
    statuses = {}
    for s, _ in results:
        statuses[s] = statuses.get(s, 0) + 1
    return {
        "name": name, "concurrency": concurrency, "total": total, "ok": ok,
        "failed": total - ok, "statuses": statuses,
        "rps": total / wall if wall else 0.0,
        "min": times[0], "mean": statistics.fmean(times), "p50": pct(times, 50),
        "p95": pct(times, 95), "p99": pct(times, 99), "max": times[-1],
    }


def incident_body(i):
    return {
        "title": f"Load test flood report {i}",
        "description": "Synthetic report created by tools/perf/load_test.py.",
        "type": "Flood",
        "latitude": 6.9 + (i % 50) * 0.001,
        "longitude": 79.85 + (i % 50) * 0.001,
        "district": "Colombo",
        "estimatedAffectedPeople": 20,
    }


def table(rows):
    out = ["| Endpoint | Concurrency | Requests | Success | Failed | Req/s | Min ms | Mean ms | p50 ms | p95 ms | p99 ms | Max ms |",
           "|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|"]
    for r in rows:
        out.append(
            f"| {r['name']} | {r['concurrency']} | {r['total']} | {r['ok'] / r['total'] * 100:.1f}% | {r['failed']} "
            f"| {r['rps']:.0f} | {r['min']:.1f} | {r['mean']:.1f} | {r['p50']:.1f} | {r['p95']:.1f} "
            f"| {r['p99']:.1f} | {r['max']:.1f} |")
    return "\n".join(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="http://localhost:5199")
    ap.add_argument("--email", required=True)
    ap.add_argument("--password", required=True)
    ap.add_argument("--reads", type=int, default=300, help="requests per read scenario and level")
    ap.add_argument("--writes", type=int, default=100, help="requests per write scenario and level")
    ap.add_argument("--levels", default="1,10,50", help="comma separated concurrency levels")
    ap.add_argument("--agent-runs", type=int, default=6, help="sequential agent runs (the analyse endpoint is rate limited to 10/min)")
    ap.add_argument("--agent-only", action="store_true", help="skip the load scenarios; measure agent latency only (use this with a real Gemini key)")
    ap.add_argument("--out", default=None, help="write the markdown report here")
    args = ap.parse_args()
    base = args.base.rstrip("/")
    levels = [int(x) for x in args.levels.split(",")]

    status, _, health = request(base, "GET", "/health")
    if status != 200:
        sys.exit(f"API not healthy at {base} (HTTP {status}). Start it first.")

    status, _, login = request(base, "POST", "/api/auth/login", body={"email": args.email, "password": args.password})
    if status != 200 or not login or "token" not in login:
        sys.exit(f"Login failed (HTTP {status}).")
    token = login["token"]

    reads = [
        ("GET /health (API + PostgreSQL probe)", "/health", None),
        ("GET /api/incidents (page 1, 20 rows)", "/api/incidents?page=1&pageSize=20", None),
        ("GET /api/incidents/statistics (aggregates)", "/api/incidents/statistics", token),
        ("GET /api/safetyzones", "/api/safetyzones", None),
        ("GET /api/incidents/nearby (geo query)", "/api/incidents/nearby?lat=6.93&lng=79.85&radiusKm=10", None),
        ("GET /api/agentruns", "/api/agentruns?take=50", token),
    ]

    rows = []
    if args.agent_only:
        reads = []
    for name, path, tok in reads:
        for level in levels:
            print(f"  {name} @ {level} ...", flush=True)
            rows.append(run_scenario(base, name, "GET", lambda i, p=path: p, tok, None, args.reads, level))
    for level in ([] if args.agent_only else levels):
        print(f"  POST /api/incidents @ {level} ...", flush=True)
        rows.append(run_scenario(base, "POST /api/incidents (write + queue analysis)", "POST",
                                 lambda i: "/api/incidents", token, incident_body, args.writes, level))

    # ---- agent latency: sequential, end to end ----
    print("  agent runs ...", flush=True)
    agent_ms, agent_status = [], []
    status, _, created = request(base, "POST", "/api/incidents", token, incident_body(0))
    incident_id = created["id"] if created and "id" in created else None
    for _ in range(args.agent_runs if incident_id else 0):
        s, secs, _ = request(base, "POST", f"/api/incidents/{incident_id}/analyse", token)
        agent_status.append(s)
        if 200 <= s < 300:
            agent_ms.append(secs * 1000)
        time.sleep(0.2)
    s, _, runs = request(base, "GET", f"/api/agentruns?incidentId={incident_id}&take=50", token) if incident_id else (0, 0, None)
    run_ms = sorted(r["durationMs"] for r in (runs or []) if r.get("status") != "Failed")
    models = sorted({r.get("model") or "—" for r in (runs or [])})

    lines = []
    lines.append(f"Run: {datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M UTC')} · target `{base}` · "
                 f"{platform.platform()} · Python {platform.python_version()}\n")
    if rows:
        lines.append(table(rows))
    lines.append("\n**Agentic AI latency** (`POST /api/incidents/{id}/analyse`, sequential, "
                 f"model(s) used: {', '.join(models)})\n")
    if agent_ms:
        a = sorted(agent_ms)
        lines.append("| Measure | Runs | Success | Min ms | Mean ms | p50 ms | Max ms |")
        lines.append("|---|--:|--:|--:|--:|--:|--:|")
        lines.append(f"| Endpoint, end to end | {len(agent_status)} | {len(a)}/{len(agent_status)} | {a[0]:.0f} | "
                     f"{statistics.fmean(a):.0f} | {pct(a, 50):.0f} | {a[-1]:.0f} |")
        if run_ms:
            lines.append(f"| Agent `DurationMs` (from `agent_runs`) | {len(run_ms)} | {len(run_ms)}/{len(run_ms)} | {run_ms[0]} | "
                         f"{statistics.fmean(run_ms):.0f} | {pct(run_ms, 50):.0f} | {run_ms[-1]} |")
    else:
        lines.append(f"No successful agent runs (statuses: {agent_status}).")
    report = "\n".join(lines)
    print("\n" + report)
    if args.out:
        with open(args.out, "w") as f:
            f.write(report + "\n")
        print(f"\nWritten to {args.out}")


if __name__ == "__main__":
    main()
