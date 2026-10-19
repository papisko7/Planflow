# Phase 5.2 Benchmark Results

Generated: 2026-10-19 13:16:34 UTC

## Scenario A — API Latency Under Load (GET /api/teams/{teamId}/tasks)

| Metric | Value | SLA | Result |
|---|---|---|---|
| Requests (ok / failed) | 3005 / 0 | - | - |
| Throughput (RPS) | 85.9 | - | - |
| Latency p50 | 11.1 ms | - | - |
| Latency p95 | 23.3 ms | < 200 ms | PASS |
| Latency p99 | 38.1 ms | - | - |
| Error rate | 0.00% | 0% | PASS |

Environment: TestServer in-memory transport (real JWT/RBAC/MediatR/EF Core pipeline, real
Testcontainers PostgreSQL, in-memory distributed cache — no real Redis). 150 distinct
simulated users (one JWT each) so the per-user rate limiter isn't what gets measured. This
measures full application-layer latency, excluding OS TCP/socket overhead a real deployed
Kestrel + network hop would add.

## Scenario B — PrioritizationJob Batch Throughput

| Dataset size | Elapsed | Tasks/sec | Allocated | Working-set delta |
|---|---|---|---|---|
| 100 | 819.2 ms | 122.1 | 5.43 MB | 1.45 MB |
| 1000 | 1221.7 ms | 818.5 | 41.92 MB | 20.61 MB |
| 10000 | 6466.5 ms | 1546.4 | 392.36 MB | 9.16 MB |

## Scenario C — SignalR Real-Time Hub Overhead

**Not measured.** The SignalR chat hub exists, but chat is an optional feature outside the mandatory
thesis scope, so its delivery overhead has no benchmark.
