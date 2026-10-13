# 0010 No cookies

Status: accepted

Blazor issues an antiforgery cookie on every page that hosts an interactive component. This app has no forms that post to the server, so `CookielessAntiforgery` replaces `IAntiforgery`: it issues no tokens and rejects unsafe form posts. Data protection keys are in memory. The theme is in `localStorage`, board history in IndexedDB. With no cookies there is no consent banner.
