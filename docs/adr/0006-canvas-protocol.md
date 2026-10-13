# 0006 Canvas coordinates, epochs and storage

Status: accepted

- The page is square (`aspect-ratio: 1`). Points are normalized to 0–1 and sent as integers 0–4095. The brush width is 0.6% of the canvas side, so every device draws the same picture.
- The backing store is CSS size times `devicePixelRatio`. A `ResizeObserver` and a `resolution` media query trigger a full redraw from stored points.
- Input: pointer events with pointer capture, `touch-action: none`, primary pointer only, left mouse button only, coalesced events when available, points closer than 3 units dropped.
- Palm rejection: touches wider or taller than 40 CSS px are ignored, and so is any touch within 1.5 s of pen input.
- Batches go out every 50 ms, at most 256 points, one call in flight. A stroke ends at 2,000 points.
- Every reset increments an epoch. Batches tagged with an old epoch are refused, so strokes in flight during a reset vanish.
- Late joiners get a snapshot of every stored stroke. The store keeps 20,000 points and evicts the oldest finished strokes first. Clients that already drew an evicted stroke keep showing it until the next reset.
- Strokes live only in memory and are lost on restart.
