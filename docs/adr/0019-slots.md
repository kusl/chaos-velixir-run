# 0019 Shared slot machine

Status: accepted

- `/slots` hosts `SlotIsland`. Results are `{ id, text }` like board messages: UUIDv7 from the server clock, five symbols from `0-9A-Z`, newest 50 kept, same offer, merge and delta rules (`MessageWindow`).
- The server draws the result with `RandomNumberGenerator` when it accepts a press, then refuses every press until `SpinMilliseconds` (default 4,000) has passed. Busy presses are checked before the per-client bucket.
- The accepting delta and late-joiner snapshots carry `spin: { id, remaining }`. Clients animate for `remaining` ms from receipt, then add the result to the feed. Because receipt is later than the server clock, a client's button only re-enables after the server lock has expired.
- A spin that arrives while an older animation runs finishes the older one at once.
- Winners: three or more of one symbol anywhere, or five strictly ascending or descending in ASCII order (digits before letters). About 1 in 50. Rules exist in `SlotRules.cs` and `SlotIsland.razor.js`; both are tested with the same cases.
- No identity. `Press` returns the new id, `busy` or `slow` to the caller only. The browser marks its own results as `mine` in IndexedDB.
- Successful presses are counted in `localStorage` (`chaos-slots-presses`). At 10 the auto spin toggle appears; it presses 200–1,000 ms after each settle, pauses while the tab is hidden, and is off on every page load.
- Reels are DOM strips moved with the Web Animations API by percentage of their own height, so no layout reads. Reduced motion swaps symbols without movement. Nothing uses inline style attributes, so the CSP is unchanged.
