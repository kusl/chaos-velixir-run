# 0011 Theme without a flash

Status: accepted

- Colors are `light-dark()` tokens on `:root`, which follow `color-scheme`. `data-theme="light"` or `"dark"` on `<html>` forces the scheme; without it the OS setting wins.
- `theme-init.js` is a tiny blocking script in `<head>` that applies the stored choice before first paint. It is an external file, so the CSP needs no hash or nonce.
- Enhanced navigation resets attributes on `<html>`; `App.razor.js` reapplies the theme on `enhancedload`, which fires in the same task as the DOM update.
- The toggle is a two-state button. Clearing site data returns to the OS setting.
