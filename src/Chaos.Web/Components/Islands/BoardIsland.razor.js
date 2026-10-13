export const DB_NAME = 'chaos-board';
export const DB_VERSION = 1;
export const STORE = 'messages';
export const LOCAL_CAP = 10000;
export const OFFER_LIMIT = 50;
export const OFFER_BYTES = 24000;
export const PAGE_SIZE = 100;
export const PING_MS = 25000;

export const migrations = [
    db => db.createObjectStore(STORE, { keyPath: 'id' }),
];

export const SCHEMA = Object.freeze({ name: DB_NAME, version: DB_VERSION, store: STORE, migrations, cap: LOCAL_CAP });

export function upgrade(db, oldVersion, newVersion = DB_VERSION, steps = migrations) {
    for (let version = oldVersion; version < newVersion; version += 1) {
        steps[version](db);
    }
}

export function timestampOf(id) {
    return Number.parseInt(id.slice(0, 8) + id.slice(9, 13), 16);
}

export function isoOf(id) {
    return new Date(timestampOf(id)).toISOString();
}

export function labelOf(id, now = Date.now()) {
    const iso = isoOf(id);
    const today = new Date(now).toISOString().slice(0, 10);
    return iso.slice(0, 10) === today ? iso.slice(11, 19) : `${iso.slice(0, 10)} ${iso.slice(11, 16)}`;
}

export function newestFirst(a, b) {
    if (a.id === b.id) {
        return 0;
    }
    return a.id < b.id ? 1 : -1;
}

export function chunkOffers(messages, maxBytes = OFFER_BYTES, limit = OFFER_LIMIT) {
    const encoder = new TextEncoder();
    const chunks = [];
    let chunk = [];
    let size = 0;
    for (const message of messages.slice(0, limit)) {
        const bytes = encoder.encode(message.id).length + encoder.encode(JSON.stringify(message.text)).length + 8;
        if (bytes > maxBytes) {
            continue;
        }
        if (chunk.length > 0 && size + bytes > maxBytes) {
            chunks.push(chunk);
            chunk = [];
            size = 0;
        }
        chunk.push(message);
        size += bytes;
    }
    if (chunk.length > 0) {
        chunks.push(chunk);
    }
    return chunks;
}

export function needsResync(version, update) {
    return update.previous !== version;
}

export function excessOf(count, cap = LOCAL_CAP) {
    return Math.max(0, count - cap);
}

function settle(request) {
    return new Promise((resolve, reject) => {
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

function finished(transaction) {
    return new Promise((resolve, reject) => {
        transaction.oncomplete = () => resolve();
        transaction.onerror = () => reject(transaction.error);
        transaction.onabort = () => reject(transaction.error);
    });
}

export function deleteDatabase(factory, name) {
    return new Promise((resolve, reject) => {
        const request = factory.deleteDatabase(name);
        request.onsuccess = () => resolve('deleted');
        request.onerror = () => reject(request.error);
        request.onblocked = () => resolve('blocked');
    });
}

export function openDatabase(factory, schema = SCHEMA, onClose = () => {}, allowReset = true) {
    return new Promise((resolve, reject) => {
        const request = factory.open(schema.name, schema.version);
        request.onupgradeneeded = event => upgrade(request.result, event.oldVersion, event.newVersion ?? schema.version, schema.migrations);
        request.onsuccess = () => {
            const db = request.result;
            db.onversionchange = () => {
                db.close();
                onClose();
            };
            db.onclose = onClose;
            resolve(db);
        };
        request.onerror = event => {
            if (allowReset && request.error?.name === 'VersionError') {
                event.preventDefault();
                deleteDatabase(factory, schema.name)
                    .then(() => openDatabase(factory, schema, onClose, false))
                    .then(resolve, reject);
                return;
            }
            reject(request.error);
        };
    });
}

export class LocalHistory {
    constructor(factory, schema = SCHEMA) {
        this.factory = factory ?? null;
        this.schema = schema;
        this.db = null;
        this.opening = null;
    }

    database() {
        if (this.db) {
            return Promise.resolve(this.db);
        }
        if (!this.factory) {
            return Promise.resolve(null);
        }
        this.opening ??= openDatabase(this.factory, this.schema, () => {
            this.db = null;
        })
            .then(db => {
                this.db = db;
                return db;
            }, () => {
                this.factory = null;
                return null;
            })
            .finally(() => {
                this.opening = null;
            });
        return this.opening;
    }

    async newest(limit) {
        const db = await this.database();
        if (!db) {
            return [];
        }
        const store = db.transaction(this.schema.store, 'readonly').objectStore(this.schema.store);
        const items = [];
        await new Promise((resolve, reject) => {
            const request = store.openCursor(null, 'prev');
            request.onsuccess = () => {
                const cursor = request.result;
                if (!cursor || items.length >= limit) {
                    resolve();
                    return;
                }
                items.push(cursor.value);
                cursor.continue();
            };
            request.onerror = () => reject(request.error);
        });
        return items;
    }

    async page(before, limit, skip) {
        const db = await this.database();
        if (!db) {
            return { items: [], last: before, done: true };
        }
        const range = before ? IDBKeyRange.upperBound(before, true) : null;
        const store = db.transaction(this.schema.store, 'readonly').objectStore(this.schema.store);
        const items = [];
        let last = before;
        let done = true;
        await new Promise((resolve, reject) => {
            const request = store.openCursor(range, 'prev');
            request.onsuccess = () => {
                const cursor = request.result;
                if (!cursor) {
                    resolve();
                    return;
                }
                if (items.length >= limit) {
                    done = false;
                    resolve();
                    return;
                }
                last = cursor.key;
                if (!skip.has(cursor.key)) {
                    items.push(cursor.value);
                }
                cursor.continue();
            };
            request.onerror = () => reject(request.error);
        });
        return { items, last, done };
    }

    async save(messages) {
        const db = await this.database();
        if (!db || messages.length === 0) {
            return;
        }
        const transaction = db.transaction(this.schema.store, 'readwrite');
        const done = finished(transaction);
        const store = transaction.objectStore(this.schema.store);
        for (const message of messages) {
            store.put(message);
        }
        const excess = excessOf(await settle(store.count()), this.schema.cap);
        if (excess > 0) {
            const keys = await settle(store.getAllKeys(null, excess));
            store.delete(IDBKeyRange.upperBound(keys[keys.length - 1]));
        }
        await done;
    }
}

class BoardClient {
    constructor(dotnet, doc, win) {
        this.dotnet = dotnet;
        this.doc = doc;
        this.win = win;
        this.history = new LocalHistory(win.indexedDB);
        this.feed = doc.getElementById('board-feed');
        this.empty = doc.getElementById('board-empty');
        this.older = doc.getElementById('board-older');
        this.announcer = doc.getElementById('board-announcer');
        this.textarea = doc.getElementById('compose-text');
        this.version = -1;
        this.live = new Map();
        this.shown = new Map();
        this.cursor = null;
        this.joining = null;
        this.timer = 0;
        this.disposed = false;
        this.onKeyDown = event => this.keyDown(event);
        this.onOlder = () => this.loadOlder();
        this.onReconnected = () => this.join();
    }

    async start() {
        this.textarea?.addEventListener('keydown', this.onKeyDown);
        this.older?.addEventListener('click', this.onOlder);
        this.win.addEventListener('chaos:reconnected', this.onReconnected);
        this.timer = this.win.setInterval(() => this.ping(), PING_MS);
        try {
            await this.offer();
        } catch {
        }
        await this.join();
    }

    keyDown(event) {
        if (event.key !== 'Enter' || !(event.ctrlKey || event.metaKey)) {
            return;
        }
        event.preventDefault();
        this.textarea.dispatchEvent(new this.win.Event('change', { bubbles: true }));
        this.textarea.form?.requestSubmit();
    }

    async offer() {
        const local = await this.history.newest(OFFER_LIMIT);
        for (const chunk of chunkOffers(local)) {
            if (this.disposed) {
                return;
            }
            await this.dotnet.invokeMethodAsync('Offer', chunk.map(message => message.id), chunk.map(message => message.text));
        }
    }

    join() {
        if (this.disposed) {
            return Promise.resolve();
        }
        this.version = -1;
        this.joining ??= this.dotnet.invokeMethodAsync('Join')
            .catch(() => {})
            .finally(() => {
                this.joining = null;
            });
        return this.joining;
    }

    async ping() {
        if (this.disposed || this.joining) {
            return;
        }
        try {
            if (!(await this.dotnet.invokeMethodAsync('Ping'))) {
                await this.join();
            }
        } catch {
        }
    }

    async apply(updates) {
        for (const update of updates) {
            if (this.disposed) {
                return;
            }
            if (update.kind === 'snapshot') {
                await this.snapshot(update);
            } else if (update.kind === 'delta' && this.version >= 0) {
                if (needsResync(this.version, update)) {
                    this.join();
                    return;
                }
                await this.delta(update);
            }
        }
    }

    async snapshot(update) {
        this.version = update.version;
        this.live = new Map(update.added.map(message => [message.id, message]));
        try {
            await this.history.save(update.added.map(message => ({ id: message.id, text: message.text })));
        } catch {
        }
        let archive = { items: [], last: null, done: true };
        try {
            archive = await this.history.page(null, PAGE_SIZE, this.live);
        } catch {
        }
        this.cursor = archive.last;
        this.render([...update.added, ...archive.items].sort(newestFirst), archive.done);
    }

    async delta(update) {
        this.version = update.version;
        for (const id of update.removed) {
            this.live.delete(id);
            this.shown.get(id)?.classList.add('archived');
        }
        const fresh = [];
        for (const message of update.added) {
            this.live.set(message.id, message);
            const existing = this.shown.get(message.id);
            if (existing) {
                existing.classList.remove('archived');
            } else {
                this.insert(message, true);
                fresh.push(message);
            }
        }
        this.updateEmpty();
        this.announce(fresh);
        try {
            await this.history.save(update.added.map(message => ({ id: message.id, text: message.text })));
        } catch {
        }
    }

    async loadOlder() {
        if (!this.older) {
            return;
        }
        this.older.disabled = true;
        try {
            const archive = await this.history.page(this.cursor, PAGE_SIZE, this.live);
            this.cursor = archive.last ?? this.cursor;
            for (const message of archive.items) {
                if (!this.shown.has(message.id)) {
                    this.insert(message, false);
                }
            }
            this.older.hidden = archive.done;
        } catch {
            this.older.hidden = true;
        } finally {
            this.older.disabled = false;
        }
    }

    item(message, fresh) {
        const element = this.doc.createElement('li');
        element.className = 'msg';
        element.classList.toggle('archived', !this.live.has(message.id));
        element.classList.toggle('fresh', fresh);
        element.dataset.id = message.id;
        const time = this.doc.createElement('time');
        time.dateTime = isoOf(message.id);
        time.textContent = labelOf(message.id);
        const text = this.doc.createElement('p');
        text.textContent = message.text;
        element.append(time, text);
        return element;
    }

    insert(message, fresh) {
        const element = this.item(message, fresh);
        let next = this.feed.firstElementChild;
        while (next && next.dataset.id > message.id) {
            next = next.nextElementSibling;
        }
        this.feed.insertBefore(element, next);
        this.shown.set(message.id, element);
    }

    render(messages, done) {
        this.shown.clear();
        const fragment = this.doc.createDocumentFragment();
        for (const message of messages) {
            const element = this.item(message, false);
            this.shown.set(message.id, element);
            fragment.append(element);
        }
        this.feed.replaceChildren(fragment);
        if (this.older) {
            this.older.hidden = done;
        }
        this.updateEmpty();
    }

    announce(messages) {
        if (!this.announcer || messages.length === 0) {
            return;
        }
        const newest = [...messages].sort(newestFirst)[0];
        this.announcer.textContent = `New message: ${newest.text.slice(0, 140)}`;
    }

    updateEmpty() {
        if (this.empty) {
            this.empty.hidden = this.shown.size > 0;
        }
    }

    dispose() {
        this.disposed = true;
        this.win.clearInterval(this.timer);
        this.textarea?.removeEventListener('keydown', this.onKeyDown);
        this.older?.removeEventListener('click', this.onOlder);
        this.win.removeEventListener('chaos:reconnected', this.onReconnected);
    }
}

export function attach(dotnet) {
    const client = new BoardClient(dotnet, document, window);
    client.start();
    return {
        apply: updates => client.apply(updates),
        dispose: () => client.dispose(),
    };
}
