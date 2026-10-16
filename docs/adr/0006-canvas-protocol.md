# 0006 Canvas coordinates, epochs and storage

Status: accepted

- The page is square (`aspect-ratio: 1`). Points are normalized to 0–1 and sent as integers 0–4095. The brush width is 0.6% of the canvas side, so every device draws the same picture.
- The backing store is CSS size times `devicePixelRatio`. A `ResizeObserver` and a `resolution` media query trigger a full redraw from stored points.
- Input: pointer events with pointer capture, `touch-action: none`, primary pointer only, left mouse button only, coalesced events when available, points closer than 3 units dropped.
- Palm rejection: touches wider or taller than 40 CSS px are ignored, and so is any touch within 1.5 s of pen input.
- Batches go out every 50 ms, at most 256 points, one call in flight. A stroke ends at 2,000 points.
- Every reset increments an epoch. Batches tagged with an old epoch are refused, so strokes in flight during a reset vanish.
- Late joiners get a snapshot of every stored stroke. The store keeps 20,000 points and evicts the oldest finished strokes first. Clients that already drew an evicted stroke keep showing it until the next reset.
- Every stroke and every reset gets a server UUIDv7 (`Uuid7.After` keeps them strictly increasing), so a stroke sorts after the reset it follows. `Draw` replies `"<result> <stroke id>"`.
- Browsers keep finished strokes in IndexedDB `chaos-canvas`: store `strokes` (`{ id, points: Uint16Array }`, cap 10,000, oldest deleted first) and store `meta` (`{ key: 'reset', id }`). A reset deletes every stroke up to its id; strokes older than the stored reset are never saved.
- Snapshots carry `reset`, `started` (server start, Unix ms) and `room` (free points). After each snapshot the browser offers its stored reset if newer than the server's and older than `started`, then its newest 500 strokes newer than both resets, older than `started`, unknown to the server and within `room`, in chunks under 24 KB.
- The server accepts an offered stroke only if it is a valid UUIDv7 from before the server started, newer than the current reset, unknown and well formed, and only while it fits: offers never evict. Accepted strokes go to everyone as a `merge` update. An adopted reset drops older strokes, bumps the epoch and sends everyone a fresh snapshot. A reset made on this server always wins over remembered ones.
- Per circuit: 500 offered strokes, `CanvasMaxPoints` offered points, 4 offered resets.
