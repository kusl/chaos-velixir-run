# 0009 Security headers, CSP, origin check

Status: accepted

- CSP: `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob:; font-src 'self'; connect-src 'self' <latency origins>; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'`. No nonce is needed.
- `blob:` images are postcard images kept in IndexedDB (ADR 0023). Uploaded images are served from `/postcard/images` with their sniffed type and `nosniff`, after `ImageSanitizer` rebuilt them.
- The latency origins come from `LatencyCatalog`, so the policy and the endpoint list cannot drift. They are allowed on every page because enhanced navigation keeps the first document's policy.
- `<ImportMap />` is left out because it emits an inline script. Modules load from fingerprinted URLs through `Assets[...]` instead.
- `Blazor.start` is called from `App.razor.js`, not an inline script. The reconnect UI is our own element, so Blazor injects no styles. No markup uses `style` attributes.
- Also sent: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Permissions-Policy` (`camera=(), microphone=(), geolocation=(self), payment=(), usb=()`; geolocation for ADR 0022), `Cross-Origin-Opener-Policy`, `Cross-Origin-Resource-Policy`, HSTS outside development. No `Server` header. `X-Frame-Options` is omitted as legacy.
- `/_blazor` requests with an `Origin` header must match the request host or `Chaos:AllowedOrigins`. Requests without `Origin` pass, since only browsers can be tricked into cross-site WebSockets.
- Headers are set in `OnStarting` so error pages keep them.
