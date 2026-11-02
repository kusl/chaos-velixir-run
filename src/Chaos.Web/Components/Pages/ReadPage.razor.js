import { openDatabase } from '../Islands/BoardIsland.razor.js';

export const DB_NAME = 'chaos-reading';
export const DB_VERSION = 1;
export const STORE = 'articles';
export const LOCAL_CAP = 1000;

export const migrations = [
    db => db.createObjectStore(STORE, { keyPath: 'id' }),
];

export const SCHEMA = Object.freeze({ name: DB_NAME, version: DB_VERSION, store: STORE, migrations, cap: LOCAL_CAP });

const ID = /^[0-9a-f]{12}$/;
const MAX_TEXT = 500;

function clip(value) {
    if (typeof value !== 'string') {
        return null;
    }
    const text = value.trim();
    return text.length > 0 ? text.slice(0, MAX_TEXT) : null;
}

export function isId(value) {
    return typeof value === 'string' && ID.test(value);
}

export function entryOf(dataset) {
    const id = dataset?.reading;
    const title = clip(dataset?.title);
    if (!isId(id) || !title) {
        return null;
    }
    return { id, title, byline: clip(dataset.byline), published: clip(dataset.published) };
}

export function remember(previous, entry, now) {
    const reads = Number.isFinite(previous?.reads) && previous.reads > 0 ? previous.reads : 0;
    const first = Number.isFinite(previous?.first) ? previous.first : now;
    return { ...entry, first, last: now, reads: reads + 1 };
}

export function recentFirst(a, b) {
    const difference = (b.last ?? 0) - (a.last ?? 0);
    if (difference !== 0) {
        return difference;
    }
    return a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
}

export function shelved(records) {
    return records.filter(record => isId(record?.id) && clip(record.title)).sort(recentFirst);
}

export function overflow(records, cap = LOCAL_CAP) {
    return shelved(records).slice(cap).map(record => record.id);
}

export function readLabel(ms, now = Date.now()) {
    if (!Number.isFinite(ms)) {
        return '';
    }
    const iso = new Date(ms).toISOString();
    const today = new Date(now).toISOString().slice(0, 10);
    return iso.slice(0, 10) === today ? `today ${iso.slice(11, 16)} UTC` : iso.slice(0, 10);
}

export function readsText(reads) {
    return reads > 1 ? `${reads} reads` : '';
}

export function hrefOf(id) {
    return `read/${id}`;
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

export async function visit(factory, entry, now = Date.now()) {
    if (!factory) {
        return [];
    }
    const db = await openDatabase(factory, SCHEMA);
    try {
        if (entry) {
            const transaction = db.transaction(STORE, 'readwrite');
            const done = finished(transaction);
            const store = transaction.objectStore(STORE);
            store.put(remember(await settle(store.get(entry.id)), entry, now));
            await done;
        }
        const records = await settle(db.transaction(STORE, 'readonly').objectStore(STORE).getAll());
        const excess = overflow(records);
        if (excess.length > 0) {
            const transaction = db.transaction(STORE, 'readwrite');
            const done = finished(transaction);
            const store = transaction.objectStore(STORE);
            for (const id of excess) {
                store.delete(id);
            }
            await done;
        }
        const gone = new Set(excess);
        return shelved(records).filter(record => !gone.has(record.id));
    } finally {
        db.close();
    }
}

function item(doc, record, current, now) {
    const element = doc.createElement('li');
    element.dataset.id = record.id;
    const link = doc.createElement('a');
    link.href = hrefOf(record.id);
    link.textContent = record.title;
    if (record.id === current) {
        link.setAttribute('aria-current', 'page');
    }
    const seen = doc.createElement('span');
    seen.className = 'seen';
    const parts = [record.byline, readLabel(record.last, now) && `Read ${readLabel(record.last, now)}`, readsText(record.reads)].filter(Boolean);
    for (const part of parts) {
        const span = doc.createElement('span');
        span.textContent = part;
        seen.append(span);
    }
    if (record.id === current) {
        const badge = doc.createElement('span');
        badge.className = 'now';
        badge.textContent = 'Reading now';
        seen.append(badge);
    }
    element.append(link, seen);
    return element;
}

export function render(doc, list, empty, records, current, now = Date.now()) {
    const fragment = doc.createDocumentFragment();
    for (const record of records) {
        fragment.append(item(doc, record, current, now));
    }
    list.replaceChildren(fragment);
    if (empty) {
        empty.hidden = records.length > 0;
    }
}

let generation = 0;

export async function track(win) {
    const doc = win.document;
    const list = doc.getElementById('reading-history');
    if (!list) {
        return;
    }
    const run = ++generation;
    const article = doc.querySelector('article[data-reading]');
    const entry = article ? entryOf(article.dataset) : null;
    let records = [];
    try {
        records = await visit(win.indexedDB, entry);
    } catch {
        records = entry ? [remember(null, entry, Date.now())] : [];
    }
    if (run !== generation || !list.isConnected) {
        return;
    }
    render(doc, list, doc.getElementById('reading-empty'), records, entry?.id ?? null);
}
