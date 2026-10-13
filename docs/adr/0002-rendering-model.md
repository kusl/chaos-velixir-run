# 0002 Static SSR with interactive islands

Status: accepted

- Every page is static server-side rendering. Navigation uses Blazor enhanced navigation and needs no circuit.
- `/board`, `/canvas` and `/slots` each host one `InteractiveServer` island (`BoardIsland`, `CanvasIsland`, `SlotIsland`). Only they open a circuit. Blazor closes the circuit when no island remains on the page.
- The board and slots feeds are rendered by the static page and then owned by the island's JavaScript module. It is marked `data-permanent` so enhanced navigation does not overwrite it. Up to 10,000 local messages never enter a server render tree.
- The compose form and the reset button are Blazor-rendered. Drawing, IndexedDB and the slot machine controls are JavaScript; `SlotIsland` renders its markup once and never re-renders.
- No bUnit: components are thin; logic lives in services tested directly, and the pages are covered by integration and Playwright tests.
