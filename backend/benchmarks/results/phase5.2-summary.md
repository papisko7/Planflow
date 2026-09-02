# Phase 5.2 Benchmark Results

Generated: 2026-09-02 09:06:15 UTC

## Scenario A — API Latency Under Load (GET /api/teams/{teamId}/tasks)

| Metric | Value | SLA | Result |
|---|---|---|---|
| Requests (ok / failed) | 3005 / 0 | - | - |
| Throughput (RPS) | 85.9 | - | - |
| Latency p50 | 0.8 ms | - | - |
| Latency p95 | 2.3 ms | < 200 ms | PASS |
| Latency p99 | 5.4 ms | - | - |
| Error rate | 0.00% | 0% | PASS |

Environment: TestServer in-memory transport (real JWT/RBAC/MediatR/EF Core pipeline, real
Testcontainers PostgreSQL, in-memory distributed cache — no real Redis). 150 distinct
simulated users (one JWT each) so the per-user rate limiter isn't what gets measured. This
measures full application-layer latency, excluding OS TCP/socket overhead a real deployed
Kestrel + network hop would add.

## Scenario B — PrioritizationJob Batch Throughput

| Dataset size | Elapsed | Tasks/sec | Allocated | Working-set delta |
|---|---|---|---|---|
| 100 | 141.2 ms | 708.1 | 4.42 MB | 1.11 MB |
| 1000 | 358.1 ms | 2792.3 | 36.58 MB | 23.35 MB |
| 10000 | 1935.0 ms | 5167.9 | 334.93 MB | 59.07 MB |

## Scenario C — SignalR Real-Time Hub Overhead

**N/A.** No SignalR hub is implemented in this codebase (Chat/real-time delivery is an [EXT],
optional feature per Use_Case_Diagram.md, not part of the mandatory ZAKRES PRACY scope governed
by CLAUDE.md). Skipped rather than building a throwaway hub solely to produce a benchmark number.
