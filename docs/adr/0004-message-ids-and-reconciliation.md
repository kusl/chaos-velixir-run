# 0004 UUIDv7 ids and handshake rules

Status: accepted

- The server creates every id with `Guid.CreateVersion7` from its own clock. Clients never mint ids. Timestamps are read from the id.
- The id string sorts in time order, so it is also the IndexedDB key and the sort key everywhere. Two posts in the same millisecond sort in random order; that is accepted.
- Handshake: the client offers its 50 newest local messages, newest first, in chunks under 24 KB so no SignalR message exceeds the 32 KB limit. The server considers at most 50 per circuit.
- An offered message is accepted only if its id is a valid UUIDv7, its time is not in the future, its time is before the server started, and its text is already normalized. Then it is merged; the server keeps the newest 50 and keeps its own copy on id ties.
- The start-time rule rejects forged recent messages: anything stamped after the server started came from this server, so if the server does not have it, it is forged or already evicted. It costs posts made to an old instance during a rolling deploy; those stay in local history.
- Posts: 500 UTF-16 code units after normalization, cut on grapheme boundaries; the same text as one of the 10 newest messages is refused.
