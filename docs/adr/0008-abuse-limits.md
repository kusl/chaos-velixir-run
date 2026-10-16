# 0008 Rate and connection limits

Status: accepted

Defaults live in `ChaosOptions` and can be overridden with `Chaos__*` environment variables. velixir's edge limits sit in front of these.

| Limit | Default |
| --- | --- |
| HTTP requests per client | 120 burst, 10 per second |
| WebSocket connections per client | 8 |
| WebSocket connections in total | 80, then 503 |
| Posts per client | 3 burst, 1 every 2 s |
| Message length | 500 |
| Offered messages per circuit | 50 |
| Stroke points per circuit | 4,000 burst, 1,500 per second |
| Points per batch / stroke / canvas | 256 / 2,048 / 20,000 |
| Resets | 1 s global cooldown, 2 burst per client |
| Slot presses | one spin at a time (4 s), 3 burst, 1 every 2 s per client |
| Slot results kept | 50 recent, 500 winners, jackpots never evicted (offers stop at 10,000) |
| Slot winner and jackpot offers per circuit | 500 each |
| Canvas offers per circuit | 500 strokes, 20,000 points, 4 resets |
| Latency tests | one at a time, 10 s apart, one per client every 300 s, 30 s probe budget |
| Latency results kept | 50 |

- A client is its IP after `X-Forwarded-For`, or its /64 for IPv6. Keys live in memory only and are never logged or shown. Devices behind one NAT are one client.
- The hub (`/_blazor`) only accepts WebSocket connections, so connection counts are real. Clients behind proxies that block WebSockets cannot use the board, canvas or slots.
- The total cap protects the 256 MB container: zen ran out of memory near 100 circuits.
- In-circuit limits use a `TimeProvider` token bucket so tests are deterministic. HTTP limits use ASP.NET Core's rate limiter.
- A press during a spin is refused before the per-client bucket, so waiting players are not penalized.
- There is no moderation. The About page says plainly that content spreads to every client and cannot be recalled.
