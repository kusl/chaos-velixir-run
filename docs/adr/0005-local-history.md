# 0005 IndexedDB local history

Status: accepted

- Database `chaos-board`, store `messages`, key path `id`, values `{ id, text }`. Slots use `chaos-slots`, store `spins`, values `{ id, text, mine }`, through the same `LocalHistory` exported by `BoardIsland.razor.js`.
- Schema changes are numbered migrations in `BoardIsland.razor.js`; `onupgradeneeded` runs every step from the stored version. A database from a newer schema (`VersionError`) is deleted and recreated, since the server can refill it.
- Other tabs close their connection on `versionchange`, so upgrades and deletes are not blocked for long.
- Cap: 10,000 messages. After each write, the oldest are deleted with one key-range delete.
- The feed shows the server window plus 100 archived messages, with a button for 100 more. Items use `content-visibility: auto`.
- Live messages have a solid border, messages the server no longer has a dotted one.
- The About page deletes both databases and the slot press count without touching the theme. The next visit to the board recreates it from the server snapshot.
- If IndexedDB is unavailable the board still works, without history.
