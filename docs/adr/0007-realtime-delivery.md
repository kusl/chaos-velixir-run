# 0007 Per-client outboxes over the circuit

Status: accepted

- Transport is the Blazor circuit itself: JS calls `[JSInvokable]` methods, the server calls the module's `apply`. No separate SignalR hub and no extra JavaScript client.
- Each island owns a bounded channel (`Outbox<T>`) drained by one pump that sends batches in order and waits for the browser to finish each batch. A full outbox, a failed call or a 20 s stall drops the subscriber.
- `attach` only wires up the module; the island calls `start` once it holds the module reference, so the first snapshot never races it. Before this, the first send could fail and nothing arrived until the 25 s ping.
- The canvas shows a loading overlay and keeps reset disabled until its first snapshot is applied.
- Subscribing enqueues a snapshot under the same lock as updates, so nothing is missed or duplicated.
- Board deltas carry `version` and `previous`; a gap triggers a fresh snapshot. Canvas updates carry an offset per stroke; a gap triggers a fresh snapshot.
- The browser pings every 25 s and resubscribes when the server says it was dropped, and resubscribes after every reconnect.
