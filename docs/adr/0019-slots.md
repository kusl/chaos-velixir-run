# 0019 Shared slot machine

Status: accepted

- `/slots` hosts `SlotIsland`. Results are `{ id, text }` like board messages: UUIDv7 from the server clock, five symbols from `0-9A-Z`, newest 50 kept, same offer, merge and delta rules (`MessageWindow`).
- The server draws the result with `RandomNumberGenerator` when it accepts a press, then refuses every press until `SpinMilliseconds` (default 4,000) has passed. Busy presses are checked before the per-client bucket.
- `SlotDrawSymbols` (default all 36) limits the symbols drawn. Tests only: `7` makes every spin `77777`. Invalid values fail options validation; the service falls back to all symbols.
- The accepting delta and late-joiner snapshots carry `spin: { id, remaining }`. Clients animate for `remaining` ms from receipt, then add the result to the feed. Because receipt is later than the server clock, a client's button only re-enables after the server lock has expired.
- A spin that arrives while an older animation runs finishes the older one at once.
- Winners: three or more of one symbol anywhere, or five strictly ascending or descending in ASCII order (digits before letters). 1,201,320 of 36⁵ = 60,466,176, about 1 in 50.3. Rules exist in `SlotRules.cs` and `SlotIsland.razor.js`; both are tested with the same cases.
- Winners window: newest `SlotWinnerCapacity` (default 500). Snapshots and deltas carry `winnersAdded` and `winnersRemoved` beside `added` and `removed`, under the same version. Clients hold back the spinning result from the winners list until its reels stop.
- Jackpots: four or five of a kind, 6,336 results, about 1 in 9,543. Kept in a third list that never evicts. Drawn jackpots are always added. Offered jackpots are added only while the list holds fewer than `SlotJackpotCapacity` (default 10,000), so forged offers cannot grow it without bound. Snapshots carry every jackpot in `jackpotsAdded`; deltas carry new ones. There is no `jackpotsRemoved`.
- A jackpot that settles on screen adds `jackpot` to the machine for 8 s: verdict `Jackpot! <kind>`, cabinet glow, fast lights, CSS confetti (24 prerendered spans). Reduced motion keeps the glow and verdict only. Screen readers hear `Jackpot!`. Late joiners and instant finishes do not celebrate.
- After restarts, clients offer their newest 500 local winners through `OfferWinners` (winners and jackpots only) and up to 500 local jackpots through `OfferJackpots` (jackpots only), each with its own per-circuit budget. Chunks stay under 24 KB.
- Local storage: IndexedDB `chaos-slots` v2. `spins` is capped at 10,000 like the board. `jackpots` (added by migration 2) is never pruned. On each visit jackpots found in `spins` are copied into it, which also migrates v1 data.
- Every winner is a `<details>`. Its odds panel is built in the browser on first open from `lesson()` in `SlotIsland.razor.js`, the only place the probability text lives. Counts are formulas checked against an exhaustive search over a six-symbol alphabet.
- No identity. `Press` returns the new id, `busy` or `slow` to the caller only. The browser marks its own results as `mine` in IndexedDB.
- Successful presses are counted in `localStorage` (`chaos-slots-presses`). At 10 the auto spin toggle appears; it presses 200–1,000 ms after each settle, pauses while the tab is hidden, and is off on every page load.
- Reels are DOM strips moved with the Web Animations API by percentage of their own height, so no layout reads. Reduced motion swaps symbols without movement. Nothing uses inline style attributes, so the CSP is unchanged.
