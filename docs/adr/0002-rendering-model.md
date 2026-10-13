# 0002 Static SSR with two interactive islands

Status: accepted

- Every page is static server-side rendering. Navigation uses Blazor enhanced navigation and needs no circuit.
- `/board` and `/canvas` each host one `InteractiveServer` island (`BoardIsland`, `CanvasIsland`). Only they open a circuit. Blazor closes the circuit when no island remains on the page.
- The board feed is rendered by the static page and then owned by the island's JavaScript module. It is marked `data-permanent` so enhanced navigation does not overwrite it. Up to 10,000 local messages never enter a server render tree.
- The compose form and the reset button are Blazor-rendered. Drawing and IndexedDB are JavaScript.
- No bUnit: components are thin; logic lives in services tested directly, and the pages are covered by integration and Playwright tests.
