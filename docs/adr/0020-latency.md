# 0020 Shared latency test

Status: accepted

- `/latency` hosts `LatencyIsland`. `LatencyCatalog` lists the endpoints: this site's `/time` (browser only), large public sites and 17 AWS DynamoDB `/ping` regions. All are anonymous GETs.
- `Begin` is refused (`busy`) while a test runs or within `LatencyGapSeconds` of the last start, then per client (`wait <seconds>`) for `LatencyCooldownSeconds`. A client is a network address (ADR 0008), so devices behind one NAT share the cooldown; the browser says so. Browser storage cannot enforce a limit, so `chaos-latency-next` is only a countdown hint. Accepted starts reply `ok <id> <cooldown>` and broadcast `running`.
- The server probes in the background (`HttpLatencyProbe`): one warm-up request, then the fastest of 3 time-to-headers, 6 endpoints at a time, 4 s per request, `LatencyBudgetSeconds` overall. `LatencyServerProbe=false` turns it off.
- The browser does the same with `fetch` (`no-cors`, `no-store`, no credentials, no referrer) and sends `Finish(id, ms[])`, -1 for failures. The server waits up to the budget plus 15 s, then publishes either way.
- A result is `{ id, text }` with text `key=browser,server;…` in integer ms, empty for none. Offers must re-serialize to the same text. Server keeps 50, browsers 10,000 (`chaos-latency`), same merge rules as slots.
- No identity: results hold only timings. The browser marks its own as `mine`. Test count (`chaos-latency-runs`) and next allowed time (`chaos-latency-next`) are in `localStorage`; 10 tests unlock auto run, which tests whenever allowed, including in hidden tabs.
