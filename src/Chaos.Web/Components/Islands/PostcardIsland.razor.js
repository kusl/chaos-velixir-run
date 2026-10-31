import { LocalHistory, POSITION_OPTIONS, announce, collectMeta, countText, excessOf, isoOf, labelOf, metaElement, newestFirst, readShare, shareDefault, writeShare } from './BoardIsland.razor.js';

export const DB_NAME = 'chaos-postcards';
export const DB_VERSION = 1;
export const CARDS = 'cards';
export const IMAGES = 'images';
export const STATE = 'state';
export const LOCAL_CAP = 10000;
export const IMAGE_BUDGET = 256 * 1024 * 1024;
export const PAGE_SIZE = 20;
export const PING_MS = 25000;
export const MAX_TEXT = 5000;
export const MAX_IMAGES = 4;
export const MAX_IMAGE_BYTES = 2 * 1024 * 1024;
export const MAX_TOTAL_BYTES = 4 * 1024 * 1024;
export const MAX_SIDE = 4096;
export const RESIZE_SIDE = 2048;
export const JPEG_QUALITY = 0.85;
export const TYPES = Object.freeze(['image/png', 'image/jpeg', 'image/gif']);
export const IMAGE_PATH = 'postcard/images';

export const migrations = [
    db => {
        db.createObjectStore(CARDS, { keyPath: 'id' });
        db.createObjectStore(IMAGES, { keyPath: 'key' });
        db.createObjectStore(STATE, { keyPath: 'key' });
    },
];

export const SCHEMA = Object.freeze({ name: DB_NAME, version: DB_VERSION, store: CARDS, migrations, cap: LOCAL_CAP });

export function imageKey(id, index) {
    return `${id}/${index}`;
}

export function imageRange(id) {
    return [`${id}/`, `${id}/￿`];
}

export function imageUrl(id, index) {
    return `${IMAGE_PATH}/${id}/${index}`;
}

export function cleanText(text) {
    const value = String(text ?? '').replace(/\r\n?/g, '\n').replace(/[\u0000-\u0009\u000b-\u001f\u007f-\u009f]/g, '');
    return typeof value.toWellFormed === 'function' ? value.toWellFormed() : value;
}

export function hasText(text) {
    return /[^\s​-‏⁠﻿]/.test(text);
}

export function fitSize(width, height, max = RESIZE_SIDE) {
    const longest = Math.max(width, height);
    if (longest <= max) {
        return { width, height };
    }
    const scale = max / longest;
    return { width: Math.max(1, Math.round(width * scale)), height: Math.max(1, Math.round(height * scale)) };
}

export function needsResize(type, size, width, height) {
    return type !== 'image/gif' && (size > MAX_IMAGE_BYTES || Math.max(width, height) > MAX_SIDE);
}

export function pickProblem(type, size, count, total) {
    if (count >= MAX_IMAGES) {
        return `Attach at most ${MAX_IMAGES} images.`;
    }
    if (!TYPES.includes(type)) {
        return 'Only PNG, JPEG and GIF images can be attached.';
    }
    if (type === 'image/gif' && size > MAX_IMAGE_BYTES) {
        return 'GIFs must be 2 MB or smaller.';
    }
    if (size !== null && total + size > MAX_TOTAL_BYTES) {
        return 'All images together can be 4 MB.';
    }
    return null;
}

export function toRecord(card, mine) {
    const record = {
        id: card.id,
        text: typeof card.text === 'string' ? card.text : '',
        images: Array.isArray(card.images)
            ? card.images.map(image => ({ type: image.type, width: image.width, height: image.height, length: image.length }))
            : [],
    };
    if (Array.isArray(card.meta) && card.meta.length > 0) {
        record.meta = card.meta;
    }
    if (mine) {
        record.mine = true;
    }
    return record;
}

export function merge(existing, record) {
    const merged = { ...record };
    if (existing?.mine || record.mine) {
        merged.mine = true;
    } else {
        delete merged.mine;
    }
    return merged;
}

export function isComplete(record, blobs) {
    return record.images.every((image, index) => blobs[index] instanceof Blob && blobs[index].size === image.length);
}

export function isStale(version, update) {
    return update.kind !== 'snapshot' && update.version <= version;
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

function walk(request, visit) {
    return new Promise((resolve, reject) => {
        request.onsuccess = () => {
            const cursor = request.result;
            if (!cursor || visit(cursor) === false) {
                resolve();
                return;
            }
            cursor.continue();
        };
        request.onerror = () => reject(request.error);
    });
}

export class PostcardHistory extends LocalHistory {
    constructor(factory, schema = SCHEMA, budget = IMAGE_BUDGET) {
        super(factory, schema);
        this.budget = budget;
    }

    async blobs(record) {
        const db = await this.database();
        if (!db || record.images.length === 0) {
            return [];
        }
        const store = db.transaction(IMAGES, 'readonly').objectStore(IMAGES);
        const values = await Promise.all(record.images.map((_, index) => settle(store.get(imageKey(record.id, index)))));
        return values.map(value => (value?.blob instanceof Blob ? value.blob : null));
    }

    async get(id) {
        const db = await this.database();
        if (!db) {
            return null;
        }
        return (await settle(db.transaction(CARDS, 'readonly').objectStore(CARDS).get(id))) ?? null;
    }

    async save(record, blobs = []) {
        const db = await this.database();
        if (!db) {
            return;
        }
        const transaction = db.transaction([CARDS, IMAGES, STATE], 'readwrite');
        const done = finished(transaction);
        const cards = transaction.objectStore(CARDS);
        const images = transaction.objectStore(IMAGES);
        const state = transaction.objectStore(STATE);
        const existing = await settle(cards.get(record.id));
        cards.put(merge(existing, record));
        let bytes = (await settle(state.get('bytes')))?.value ?? 0;
        for (let index = 0; index < blobs.length; index += 1) {
            const blob = blobs[index];
            if (!(blob instanceof Blob)) {
                continue;
            }
            const key = imageKey(record.id, index);
            if ((await settle(images.count(key))) === 0) {
                images.put({ key, id: record.id, blob });
                bytes += blob.size;
            }
        }
        const excess = excessOf(await settle(cards.count()), this.schema.cap);
        if (excess > 0) {
            const keys = await settle(cards.getAllKeys(null, excess));
            const last = keys[keys.length - 1];
            await walk(images.openCursor(IDBKeyRange.upperBound(`${last}/￿`)), cursor => {
                bytes -= cursor.value?.blob?.size ?? 0;
                cursor.delete();
            });
            cards.delete(IDBKeyRange.upperBound(last));
        }
        if (bytes > this.budget) {
            const [keep] = imageRange(record.id);
            await walk(images.openCursor(IDBKeyRange.upperBound(keep, true)), cursor => {
                if (bytes <= this.budget) {
                    return false;
                }
                bytes -= cursor.value?.blob?.size ?? 0;
                cursor.delete();
                return true;
            });
        }
        state.put({ key: 'bytes', value: Math.max(0, bytes) });
        await done;
    }
}

class ShareControl {
    constructor(win, box, input, status) {
        this.win = win;
        this.box = box;
        this.input = input;
        this.status = status;
        this.permission = null;
        this.disposed = false;
        this.onChange = () => this.toggle();
        this.onPermission = () => this.permissionChanged();
    }

    geolocation() {
        return this.win.navigator?.geolocation ?? null;
    }

    async setup() {
        if (!this.input || !this.box || !this.geolocation()) {
            return;
        }
        let state = null;
        try {
            this.permission = await this.win.navigator.permissions?.query({ name: 'geolocation' }) ?? null;
            state = this.permission?.state ?? null;
        } catch {
            this.permission = null;
        }
        if (this.disposed) {
            return;
        }
        this.permission?.addEventListener('change', this.onPermission);
        this.input.checked = shareDefault(readShare(this.win.localStorage), state);
        this.input.addEventListener('change', this.onChange);
        this.box.hidden = false;
    }

    say(text) {
        if (this.status) {
            this.status.textContent = text;
        }
    }

    permissionChanged() {
        if (this.permission?.state === 'denied' && this.input?.checked) {
            this.input.checked = false;
            this.say('Location permission was turned off, so nothing is attached.');
        }
    }

    position() {
        return new Promise((resolve, reject) => {
            const geolocation = this.geolocation();
            if (!geolocation) {
                reject(new Error('Geolocation is not available.'));
                return;
            }
            geolocation.getCurrentPosition(resolve, reject, POSITION_OPTIONS);
        });
    }

    async toggle() {
        if (!this.input.checked) {
            writeShare(this.win.localStorage, 'off');
            this.say('Nothing is attached to your postcards.');
            return;
        }
        this.input.disabled = true;
        this.say('Asking for your location.');
        try {
            await this.position();
            writeShare(this.win.localStorage, 'on');
            this.say('Your location and browser details will be attached to your postcards.');
        } catch {
            this.input.checked = false;
            this.say('No location permission, so nothing is attached.');
        } finally {
            this.input.disabled = false;
        }
    }

    async meta() {
        if (this.disposed || !this.input?.checked) {
            return null;
        }
        try {
            return collectMeta(await this.position(), this.win);
        } catch (error) {
            if (error?.code === 1) {
                this.input.checked = false;
            }
            this.say('Could not read your location, so that postcard has no details.');
            return null;
        }
    }

    dispose() {
        this.disposed = true;
        this.input?.removeEventListener('change', this.onChange);
        this.permission?.removeEventListener('change', this.onPermission);
    }
}

class PostcardClient {
    constructor(dotnet, doc, win) {
        this.dotnet = dotnet;
        this.doc = doc;
        this.win = win;
        this.history = new PostcardHistory(win.indexedDB);
        this.form = doc.getElementById('postcard-form');
        this.textarea = doc.getElementById('postcard-text');
        this.files = doc.getElementById('postcard-files');
        this.picked = doc.getElementById('postcard-picked');
        this.status = doc.getElementById('postcard-status');
        this.count = doc.getElementById('postcard-count');
        this.button = doc.getElementById('postcard-send');
        this.feed = doc.getElementById('postcard-feed');
        this.empty = doc.getElementById('postcard-empty');
        this.older = doc.getElementById('postcard-older');
        this.share = new ShareControl(win, doc.getElementById('postcard-share-box'), doc.getElementById('postcard-share'), doc.getElementById('postcard-share-status'));
        this.attachments = [];
        this.version = -1;
        this.live = null;
        this.shown = new Map();
        this.records = new Map();
        this.urls = new Map();
        this.mine = new Set();
        this.cursor = null;
        this.joining = null;
        this.offered = false;
        this.sending = false;
        this.timer = 0;
        this.disposed = false;
        this.onSubmit = event => this.submit(event);
        this.onKeyDown = event => this.keyDown(event);
        this.onInput = () => this.recount();
        this.onFiles = () => this.addFiles();
        this.onPicked = event => this.removePicked(event);
        this.onOlder = () => this.loadOlder();
        this.onReconnected = () => this.join();
    }

    async start() {
        this.form?.addEventListener('submit', this.onSubmit);
        this.textarea?.addEventListener('keydown', this.onKeyDown);
        this.textarea?.addEventListener('input', this.onInput);
        this.files?.addEventListener('change', this.onFiles);
        this.picked?.addEventListener('click', this.onPicked);
        this.older?.addEventListener('click', this.onOlder);
        this.win.addEventListener('chaos:reconnected', this.onReconnected);
        this.timer = this.win.setInterval(() => this.ping(), PING_MS);
        this.recount();
        for (const control of [this.textarea, this.files, this.button]) {
            if (control) {
                control.disabled = false;
            }
        }
        try {
            await this.share.setup();
        } catch {
        }
        await this.join();
    }

    say(text) {
        if (this.status && !this.disposed) {
            this.status.textContent = text ?? '';
        }
    }

    recount() {
        if (!this.count || !this.textarea || this.disposed) {
            return;
        }
        const max = this.textarea.maxLength;
        const length = this.textarea.value.length;
        this.count.textContent = countText(length, max);
        this.count.classList.toggle('full', max > 0 && length >= max);
    }

    keyDown(event) {
        if (event.key !== 'Enter' || !(event.ctrlKey || event.metaKey)) {
            return;
        }
        event.preventDefault();
        this.form?.requestSubmit();
    }

    totalBytes() {
        return this.attachments.reduce((sum, item) => sum + item.blob.size, 0);
    }

    async prepare(file) {
        const problem = pickProblem(file.type, file.type === 'image/gif' ? file.size : null, this.attachments.length, this.totalBytes());
        if (problem) {
            return { problem };
        }
        if (file.type === 'image/gif') {
            return { blob: file };
        }
        let bitmap;
        try {
            bitmap = await this.win.createImageBitmap(file);
        } catch {
            return { problem: 'That file is not an image this browser can read.' };
        }
        try {
            if (!needsResize(file.type, file.size, bitmap.width, bitmap.height)) {
                return this.checkTotal(file);
            }
            const size = fitSize(bitmap.width, bitmap.height);
            const canvas = this.doc.createElement('canvas');
            canvas.width = size.width;
            canvas.height = size.height;
            const context = canvas.getContext('2d');
            context.fillStyle = '#ffffff';
            context.fillRect(0, 0, size.width, size.height);
            context.drawImage(bitmap, 0, 0, size.width, size.height);
            const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', JPEG_QUALITY));
            if (!blob || blob.size > MAX_IMAGE_BYTES) {
                return { problem: 'That image is too large even after scaling it down.' };
            }
            return this.checkTotal(blob);
        } finally {
            bitmap.close();
        }
    }

    checkTotal(blob) {
        return this.totalBytes() + blob.size > MAX_TOTAL_BYTES ? { problem: 'All images together can be 4 MB.' } : { blob };
    }

    async addFiles() {
        const files = [...(this.files?.files ?? [])];
        if (this.files) {
            this.files.value = '';
        }
        let problem = null;
        for (const file of files) {
            const result = await this.prepare(file);
            if (this.disposed) {
                return;
            }
            if (result.blob) {
                this.attachments.push({ blob: result.blob, name: file.name, url: this.win.URL.createObjectURL(result.blob) });
            } else {
                problem ??= result.problem;
            }
        }
        this.renderPicked();
        this.say(problem);
    }

    renderPicked() {
        if (!this.picked) {
            return;
        }
        const items = this.attachments.map((item, index) => {
            const row = this.doc.createElement('li');
            const image = this.doc.createElement('img');
            image.src = item.url;
            image.alt = item.name;
            const remove = this.doc.createElement('button');
            remove.type = 'button';
            remove.dataset.index = String(index);
            remove.textContent = 'Remove';
            remove.setAttribute('aria-label', `Remove image ${index + 1}`);
            row.append(image, remove);
            return row;
        });
        this.picked.replaceChildren(...items);
    }

    removePicked(event) {
        const button = event.target instanceof this.win.Element ? event.target.closest('button[data-index]') : null;
        if (!button || this.sending) {
            return;
        }
        const [removed] = this.attachments.splice(Number(button.dataset.index), 1);
        if (removed) {
            this.win.URL.revokeObjectURL(removed.url);
        }
        this.renderPicked();
        this.say('');
    }

    clearComposer() {
        for (const item of this.attachments) {
            this.win.URL.revokeObjectURL(item.url);
        }
        this.attachments = [];
        this.renderPicked();
        if (this.textarea) {
            this.textarea.value = '';
        }
        this.recount();
    }

    async submit(event) {
        event.preventDefault();
        if (this.sending || this.disposed || !this.textarea) {
            return;
        }
        const text = cleanText(this.textarea.value);
        if (!hasText(text) && this.attachments.length === 0) {
            this.say('Write something or add an image first.');
            return;
        }
        this.sending = true;
        if (this.button) {
            this.button.disabled = true;
        }
        this.say(this.attachments.length > 0 ? 'Sending your postcard and its images.' : 'Sending your postcard.');
        let streams = [];
        try {
            const meta = await this.share.meta();
            streams = this.attachments.map(item => this.win.DotNet.createJSStreamReference(item.blob));
            const reply = await this.dotnet.invokeMethodAsync('Send', text, streams, meta);
            if (typeof reply?.id === 'string') {
                this.clearComposer();
                this.say('');
                await this.own(reply.id);
            } else {
                this.say(reply?.status ?? 'Could not send. Try again.');
            }
        } catch {
            this.say('Could not send. Try again.');
        } finally {
            this.sending = false;
            if (this.button && !this.disposed) {
                this.button.disabled = false;
            }
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
            if (isStale(this.version, update) || (update.kind !== 'snapshot' && this.version < 0)) {
                continue;
            }
            this.version = update.version;
            if (update.kind === 'snapshot') {
                await this.snapshot(update.card ?? null);
            } else {
                await this.change(update.card ?? null);
            }
        }
    }

    async stored(id) {
        try {
            return await this.history.get(id);
        } catch {
            return null;
        }
    }

    async storedBlobs(record) {
        try {
            return await this.history.blobs(record);
        } catch {
            return [];
        }
    }

    async save(record, blobs) {
        try {
            await this.history.save(record, blobs);
        } catch {
            try {
                await this.history.save(record, []);
            } catch {
            }
        }
    }

    async snapshot(card) {
        this.live = card?.id ?? null;
        let liveRecord = null;
        if (card) {
            const existing = await this.stored(card.id);
            liveRecord = merge(existing, toRecord(card, this.mine.has(card.id)));
            await this.save(liveRecord, []);
        }
        let archive = { items: [], last: null, done: true };
        try {
            archive = await this.history.page(null, PAGE_SIZE, new Set(this.live ? [this.live] : []));
        } catch {
        }
        this.cursor = archive.last;
        const records = liveRecord ? [liveRecord, ...archive.items] : archive.items;
        this.remember(records);
        await this.render(records.sort(newestFirst), archive.done);
        if (liveRecord) {
            this.complete(liveRecord).catch(() => {});
        } else {
            await this.offerBack(archive.items[0] ?? null);
        }
    }

    async change(card) {
        const previous = this.live;
        this.live = card?.id ?? null;
        if (previous && previous !== this.live) {
            this.shown.get(previous)?.classList.add('archived');
        }
        if (!card) {
            return;
        }
        const record = merge(await this.stored(card.id), toRecord(card, this.mine.has(card.id)));
        this.remember([record]);
        const existing = this.shown.get(card.id);
        if (existing) {
            existing.classList.remove('archived');
        } else {
            await this.insert(record, true);
            this.announce(record);
        }
        this.updateEmpty();
        await this.save(record, []);
        this.complete(record).catch(() => {});
    }

    async complete(record) {
        const blobs = await this.storedBlobs(record);
        if (isComplete(record, blobs)) {
            return;
        }
        const fetched = await Promise.all(record.images.map((image, index) => (blobs[index] ? Promise.resolve(blobs[index]) : this.download(record.id, index, image))));
        await this.save(record, fetched);
        if (this.disposed) {
            return;
        }
        const element = this.shown.get(record.id);
        if (element) {
            this.paint(element, record, fetched);
        }
    }

    async download(id, index, image) {
        try {
            const response = await this.win.fetch(new URL(imageUrl(id, index), this.doc.baseURI), { cache: 'force-cache', credentials: 'omit' });
            if (!response.ok) {
                return null;
            }
            const blob = await response.blob();
            return blob.size === image.length ? new Blob([blob], { type: image.type }) : null;
        } catch {
            return null;
        }
    }

    async offerBack(newest) {
        if (this.offered || !newest || this.disposed) {
            return;
        }
        this.offered = true;
        const blobs = await this.storedBlobs(newest);
        if (!isComplete(newest, blobs)) {
            return;
        }
        try {
            const streams = newest.images.map((_, index) => this.win.DotNet.createJSStreamReference(blobs[index]));
            await this.dotnet.invokeMethodAsync('Offer', newest.id, newest.text, newest.meta ?? null, streams);
        } catch {
        }
    }

    async own(id) {
        if (this.disposed || typeof id !== 'string') {
            return;
        }
        this.mine.add(id);
        const record = this.records.get(id) ?? (await this.stored(id));
        if (!record) {
            return;
        }
        const mine = { ...record, mine: true };
        this.records.set(id, mine);
        const existing = this.shown.get(id);
        if (existing && !existing.classList.contains('mine')) {
            existing.classList.add('mine');
            existing.querySelector('.head')?.append(this.yours());
        }
        await this.save(mine, []);
    }

    remember(records) {
        for (const record of records) {
            this.records.set(record.id, record);
            if (record.mine) {
                this.mine.add(record.id);
            }
        }
    }

    async loadOlder() {
        if (!this.older) {
            return;
        }
        this.older.disabled = true;
        try {
            const archive = await this.history.page(this.cursor, PAGE_SIZE, new Set(this.live ? [this.live] : []));
            this.cursor = archive.last ?? this.cursor;
            this.remember(archive.items);
            for (const record of archive.items) {
                if (!this.shown.has(record.id)) {
                    await this.insert(record, false);
                }
            }
            this.older.hidden = archive.done;
        } catch {
            this.older.hidden = true;
        } finally {
            this.older.disabled = false;
        }
    }

    revoke(id) {
        for (const url of this.urls.get(id) ?? []) {
            this.win.URL.revokeObjectURL(url);
        }
        this.urls.delete(id);
    }

    paint(element, record, blobs) {
        const box = element.querySelector('.images');
        if (!box) {
            return;
        }
        this.revoke(record.id);
        const urls = [];
        const total = record.images.length;
        const children = record.images.map((image, index) => {
            const blob = blobs[index];
            if (blob instanceof Blob) {
                const img = this.doc.createElement('img');
                const url = this.win.URL.createObjectURL(blob);
                urls.push(url);
                img.src = url;
                img.width = image.width;
                img.height = image.height;
                img.alt = `Image ${index + 1} of ${total}`;
                img.decoding = 'async';
                return img;
            }
            const missing = this.doc.createElement('span');
            missing.className = 'missing';
            missing.textContent = blob === undefined && record.id === this.live
                ? `Loading image ${index + 1} of ${total}`
                : `Image ${index + 1} of ${total} was not kept`;
            return missing;
        });
        this.urls.set(record.id, urls);
        box.replaceChildren(...children);
    }

    yours() {
        const yours = this.doc.createElement('span');
        yours.className = 'yours';
        yours.textContent = 'Your message';
        return yours;
    }

    async item(record, fresh) {
        const mine = this.mine.has(record.id) || record.mine === true;
        const element = this.doc.createElement('li');
        element.className = 'card';
        element.classList.toggle('archived', record.id !== this.live);
        element.classList.toggle('fresh', fresh);
        element.classList.toggle('mine', mine);
        element.dataset.id = record.id;
        const head = this.doc.createElement('div');
        head.className = 'head';
        const time = this.doc.createElement('time');
        time.dateTime = isoOf(record.id);
        time.textContent = labelOf(record.id);
        head.append(time);
        if (mine) {
            head.append(this.yours());
        }
        element.append(head);
        if (record.text) {
            const text = this.doc.createElement('p');
            text.className = 'text';
            text.textContent = record.text;
            element.append(text);
        }
        if (record.images.length > 0) {
            const box = this.doc.createElement('div');
            box.className = 'images';
            element.append(box);
            const blobs = await this.storedBlobs(record);
            this.paint(element, record, record.images.map((_, index) => blobs[index] ?? (record.id === this.live ? undefined : null)));
        }
        const details = metaElement(this.doc, record.meta);
        if (details) {
            element.append(details);
        }
        return element;
    }

    async insert(record, fresh) {
        const element = await this.item(record, fresh);
        if (this.shown.has(record.id) || this.disposed) {
            this.revoke(record.id);
            return;
        }
        let next = this.feed.firstElementChild;
        while (next && next.dataset.id > record.id) {
            next = next.nextElementSibling;
        }
        this.feed.insertBefore(element, next);
        this.shown.set(record.id, element);
        this.updateEmpty();
    }

    async render(records, done) {
        for (const id of [...this.urls.keys()]) {
            this.revoke(id);
        }
        this.shown.clear();
        const elements = [];
        for (const record of records) {
            const element = await this.item(record, false);
            this.shown.set(record.id, element);
            elements.push(element);
        }
        if (this.disposed) {
            return;
        }
        this.feed.replaceChildren(...elements);
        if (this.older) {
            this.older.hidden = done;
        }
        this.updateEmpty();
    }

    announce(record) {
        if (this.disposed || !this.feed?.isConnected) {
            return;
        }
        const images = record.images.length;
        const suffix = images === 0 ? '' : images === 1 ? ' (1 image)' : ` (${images} images)`;
        announce(this.doc, `New postcard${suffix}: ${record.text.slice(0, 140)}`);
    }

    updateEmpty() {
        if (this.empty) {
            this.empty.hidden = this.shown.size > 0;
        }
    }

    dispose() {
        this.disposed = true;
        this.win.clearInterval(this.timer);
        this.share.dispose();
        this.form?.removeEventListener('submit', this.onSubmit);
        this.textarea?.removeEventListener('keydown', this.onKeyDown);
        this.textarea?.removeEventListener('input', this.onInput);
        this.files?.removeEventListener('change', this.onFiles);
        this.picked?.removeEventListener('click', this.onPicked);
        this.older?.removeEventListener('click', this.onOlder);
        this.win.removeEventListener('chaos:reconnected', this.onReconnected);
        for (const item of this.attachments) {
            this.win.URL.revokeObjectURL(item.url);
        }
        for (const id of [...this.urls.keys()]) {
            this.revoke(id);
        }
    }
}

export function attach(dotnet) {
    const client = new PostcardClient(dotnet, document, window);
    client.start();
    return {
        apply: updates => client.apply(updates),
        dispose: () => client.dispose(),
    };
}
