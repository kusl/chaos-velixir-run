# 0012 Reconnect, backoff, cold start

Status: accepted

- The free plan sleeps after 15 idle minutes and restarts without warning.
- Retries: first at once, then exponential with equal jitter (half fixed, half random), capped at 30 s, 30 attempts (about ten minutes).
- `ReconnectBanner` is a non-modal banner driven by Blazor's `components-reconnect-*` classes: "Reconnecting" on the first attempt, "Waking up the server" after it, then a retry button. Retrying also happens when the tab becomes visible again.
- A rejected reconnect means the server lost the circuit, usually a restart, so the page reloads. The islands then rebuild from the server and IndexedDB.
- An unhandled error shows a separate error bar with a reload link.
