# 0002 Static SSR with interactive islands

Status: accepted

- Every page is static server-side rendering. Navigation uses Blazor enhanced navigation and needs no circuit.
- `/board`, `/canvas`, `/slots` and `/latency` each host one `InteractiveServer` island (`BoardIsland`, `CanvasIsland`, `SlotIsland`, `LatencyIsland`). Only they open a circuit. Blazor closes the circuit when no island remains on the page.
- The board, slots and latency feeds are rendered by the static page and then owned by the island's JavaScript module. It is marked `data-permanent` so enhanced navigation does not overwrite it. Up to 10,000 local messages never enter a server render tree.
- Enhanced navigation morphs elements of the old page into the new one by tag and position. JavaScript never writes into page markup outside an island or a `data-permanent` element: the board once announced into a trailing `<p>` that became the canvas page's note. Screen reader announcements go to one `#announcer` in the layout (`data-permanent`, cleared on `enhancedload`), and modules stop announcing once disposed or detached.
- The compose form and the reset button are Blazor-rendered. The character count is an empty island element that JavaScript fills on `input`; the island asks for a recount after a post clears the box. Drawing, IndexedDB and the slot machine controls are JavaScript; `SlotIsland` renders its markup once and never re-renders.
- No bUnit: components are thin; logic lives in services tested directly, and the pages are covered by integration and Playwright tests.
