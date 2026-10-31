# 0022 Optional location and browser details on board messages

Status: accepted

- Postcards use the same fields, rules and setting (ADR 0023).

- Opt-in. `#compose-share` appears only when `navigator.geolocation` exists. Ticking it calls `getCurrentPosition`; the browser prompt is the consent. Without permission nothing changes.
- Default state: on when the Permissions API reports `granted` and `localStorage` `chaos-board-share` is not `off`; off when denied. Unticking stores `off`. A permission change to `denied` unticks it. Clearing local history removes the key.
- On send, `BoardIsland` asks the module for `meta()` (15 s server timeout, `maximumAge` 60 s, 10 s position timeout). Failure sends the message without details.
- `collectMeta` reads only the browser: coordinates, accuracy, altitude, heading, speed, fix time, `Intl` time zone, locale, calendar, numbering system, UTC offset, local time, languages, user agent, client hints brands, platform, mobile, vendor, webdriver, Do Not Track, GPC, PDF viewer, screen, available screen, viewport, pixel ratio, color depth, orientation, threads, memory, touch points, pointer, color scheme, reduced motion, contrast, online, connection type, downlink, RTT, save data.
- No reverse geocoding, no third-party requests. The server adds nothing: no IP, no headers.
- Wire and storage: `BoardMessage.meta` is an ordered list of `{ key, value }` strings, omitted when null. `MessageMeta` keeps known keys only, in catalog order, first occurrence wins, strips control characters, trims, cuts to a per-key length and range-checks numeric keys. Without valid latitude and longitude the list is dropped. Offers carry `meta` and must already be normalized.
- Values are self-reported and unverified.
- Labels live in `MessageMeta.Fields` and `META_FIELDS`; tests pin the same key order in both.
- `Submit` returns the new message; the island calls `sent(id)`, which clears the composer and recounts in the browser before Blazor re-renders, then calls `own(id)` so the browser marks it `mine` in IndexedDB and shows `Your message`. The server keeps no ownership. Older messages are unaffected.
- Rendering: collapsed `<details class="meta">` under the text, timestamp-sized and muted, both in the prerender and in the module.
