# 0013 Memory and CPU settings

Status: accepted

- Workstation, non-concurrent GC, `ConserveMemory=7`, no tiered PGO, invariant globalization, English resources only.
- Disconnected circuits: 20 kept for one minute. Persisted circuits: 20 for five minutes. Two unacknowledged render batches per circuit.
- WebSocket compression is off: bandwidth is unmetered, CPU is a quarter core.
- Response compression (Brotli, gzip) at the fastest level for HTML; static assets are precompressed at build time.
- No ReadyToRun and no trimming: velixir runs a portable `dotnet publish` and its CPU architecture is not documented, the framework is already precompiled, and Blazor Server is not trim-safe.
- Logs at `Warning`, except host lifetime messages.
- `/health` is a built-in health check, exempt from rate limiting.
- Shutdown waits up to 10 s.
