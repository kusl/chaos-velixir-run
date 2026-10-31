export const DB_NAME = 'chaos-board';
export const DB_VERSION = 1;
export const STORE = 'messages';
export const LOCAL_CAP = 10000;
export const OFFER_LIMIT = 50;
export const OFFER_BYTES = 24000;
export const PAGE_SIZE = 100;
export const PING_MS = 25000;
export const SHARE_KEY = 'chaos-board-share';
export const POSITION_OPTIONS = Object.freeze({ enableHighAccuracy: false, maximumAge: 60000, timeout: 10000 });

export const META_FIELDS = Object.freeze([
    ['latitude', 'Latitude'],
    ['longitude', 'Longitude'],
    ['accuracy', 'Accuracy (m)'],
    ['altitude', 'Altitude (m)'],
    ['altitudeAccuracy', 'Altitude accuracy (m)'],
    ['heading', 'Heading (°)'],
    ['speed', 'Speed (m/s)'],
    ['positionAt', 'Position time'],
    ['timeZone', 'Time zone'],
    ['utcOffset', 'UTC offset'],
    ['localTime', 'Local time'],
    ['locale', 'Locale'],
    ['calendar', 'Calendar'],
    ['numberingSystem', 'Numbering system'],
    ['language', 'Language'],
    ['languages', 'Languages'],
    ['userAgent', 'User agent'],
    ['brands', 'Brands'],
    ['platform', 'Platform'],
    ['mobile', 'Mobile'],
    ['vendor', 'Vendor'],
    ['webdriver', 'Automated'],
    ['doNotTrack', 'Do Not Track'],
    ['globalPrivacyControl', 'Global Privacy Control'],
    ['pdfViewer', 'PDF viewer'],
    ['screen', 'Screen'],
    ['availableScreen', 'Available screen'],
    ['viewport', 'Viewport'],
    ['pixelRatio', 'Pixel ratio'],
    ['colorDepth', 'Color depth'],
    ['orientation', 'Orientation'],
    ['cores', 'CPU threads'],
    ['memory', 'Memory (GB)'],
    ['touchPoints', 'Touch points'],
    ['pointer', 'Pointer'],
    ['colorScheme', 'Color scheme'],
    ['reducedMotion', 'Reduced motion'],
    ['contrast', 'Contrast'],
    ['online', 'Online'],
    ['connection', 'Connection'],
    ['downlink', 'Downlink (Mb/s)'],
    ['rtt', 'Round trip (ms)'],
    ['saveData', 'Data saver'],
]);

const META_LABELS = new Map(META_FIELDS);

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
        const meta = message.meta ? encoder.encode(JSON.stringify(message.meta)).length : 0;
        const bytes = encoder.encode(message.id).length + encoder.encode(JSON.stringify(message.text)).length + meta + 8;
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

export function countText(length, max) {
    return max > 0 ? `${length} / ${max}` : String(length);
}

export function announce(doc, text) {
    const announcer = doc.getElementById('announcer');
    if (announcer) {
        announcer.textContent = text;
    }
}

export function excessOf(count, cap = LOCAL_CAP) {
    return Math.max(0, count - cap);
}

export function metaLabel(key) {
    return META_LABELS.get(key) ?? key;
}

export function readShare(storage) {
    try {
        const value = storage?.getItem(SHARE_KEY);
        return value === 'on' || value === 'off' ? value : null;
    } catch {
        return null;
    }
}

export function writeShare(storage, value) {
    try {
        storage?.setItem(SHARE_KEY, value);
    } catch {
    }
}

export function shareDefault(choice, state) {
    if (choice === 'off' || state === 'denied') {
        return false;
    }
    return state === 'granted' || (state === null && choice === 'on');
}

export function formatOffset(minutesEast) {
    if (!Number.isFinite(minutesEast)) {
        return null;
    }
    const sign = minutesEast < 0 ? '-' : '+';
    const total = Math.abs(Math.trunc(minutesEast));
    return `${sign}${String(Math.trunc(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`;
}

export function localStamp(date) {
    const pad = (value, length = 2) => String(value).padStart(length, '0');
    return `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

function numberText(value) {
    return typeof value === 'number' && Number.isFinite(value) ? String(value) : null;
}

function flag(value) {
    return typeof value === 'boolean' ? (value ? 'yes' : 'no') : null;
}

function size(width, height) {
    return Number.isFinite(width) && Number.isFinite(height) ? `${width}x${height}` : null;
}

function resolvedIntl(win) {
    try {
        return (win.Intl ?? Intl).DateTimeFormat().resolvedOptions();
    } catch {
        return {};
    }
}

function mediaQuery(win) {
    return query => {
        try {
            return typeof win.matchMedia === 'function' ? win.matchMedia(query).matches === true : null;
        } catch {
            return null;
        }
    };
}

function firstMatch(media, name, values, fallback) {
    for (const value of values) {
        const matched = media(`(${name}: ${value})`);
        if (matched === null) {
            return null;
        }
        if (matched) {
            return value;
        }
    }
    return fallback;
}

export function collectMeta(position, win, now = new Date()) {
    const coords = position?.coords;
    if (!coords || !Number.isFinite(coords.latitude) || !Number.isFinite(coords.longitude)) {
        return null;
    }
    const nav = win?.navigator ?? {};
    const agent = nav.userAgentData;
    const connection = nav.connection;
    const screen = win?.screen;
    const intl = resolvedIntl(win ?? {});
    const media = mediaQuery(win ?? {});
    const values = {
        latitude: numberText(coords.latitude),
        longitude: numberText(coords.longitude),
        accuracy: numberText(coords.accuracy),
        altitude: numberText(coords.altitude),
        altitudeAccuracy: numberText(coords.altitudeAccuracy),
        heading: numberText(coords.heading),
        speed: numberText(coords.speed),
        positionAt: Number.isFinite(position.timestamp) ? new Date(position.timestamp).toISOString() : null,
        timeZone: intl.timeZone,
        utcOffset: formatOffset(-now.getTimezoneOffset()),
        localTime: localStamp(now),
        locale: intl.locale,
        calendar: intl.calendar,
        numberingSystem: intl.numberingSystem,
        language: nav.language,
        languages: Array.isArray(nav.languages) ? nav.languages.join(', ') : null,
        userAgent: nav.userAgent,
        brands: Array.isArray(agent?.brands) ? agent.brands.map(brand => `${brand.brand} ${brand.version}`).join(', ') : null,
        platform: agent?.platform || nav.platform,
        mobile: flag(agent?.mobile),
        vendor: nav.vendor,
        webdriver: flag(nav.webdriver),
        doNotTrack: nav.doNotTrack,
        globalPrivacyControl: flag(nav.globalPrivacyControl),
        pdfViewer: flag(nav.pdfViewerEnabled),
        screen: size(screen?.width, screen?.height),
        availableScreen: size(screen?.availWidth, screen?.availHeight),
        viewport: size(win?.innerWidth, win?.innerHeight),
        pixelRatio: numberText(win?.devicePixelRatio),
        colorDepth: numberText(screen?.colorDepth),
        orientation: screen?.orientation?.type,
        cores: numberText(nav.hardwareConcurrency),
        memory: numberText(nav.deviceMemory),
        touchPoints: numberText(nav.maxTouchPoints),
        pointer: firstMatch(media, 'pointer', ['coarse', 'fine'], 'none'),
        colorScheme: firstMatch(media, 'prefers-color-scheme', ['dark'], 'light'),
        reducedMotion: flag(media('(prefers-reduced-motion: reduce)')),
        contrast: firstMatch(media, 'prefers-contrast', ['more', 'less'], 'no preference'),
        online: flag(nav.onLine),
        connection: [connection?.effectiveType, connection?.type].filter(value => typeof value === 'string' && value.length > 0).join(', ') || null,
        downlink: numberText(connection?.downlink),
        rtt: numberText(connection?.rtt),
        saveData: flag(connection?.saveData),
    };
    const entries = [];
    for (const [key] of META_FIELDS) {
        const value = values[key];
        if (typeof value === 'string' && value.trim().length > 0) {
            entries.push({ key, value });
        }
    }
    return entries;
}

export function toRecord(message, mine) {
    const record = { id: message.id, text: message.text };
    if (Array.isArray(message.meta) && message.meta.length > 0) {
        record.meta = message.meta;
    }
    if (mine) {
        record.mine = true;
    }
    return record;
}

export function metaElement(doc, meta) {
    if (!Array.isArray(meta) || meta.length === 0) {
        return null;
    }
    const details = doc.createElement('details');
    details.className = 'meta';
    const summary = doc.createElement('summary');
    summary.textContent = 'Details';
    const list = doc.createElement('dl');
    for (const entry of meta) {
        const row = doc.createElement('div');
        const term = doc.createElement('dt');
        term.textContent = metaLabel(entry.key);
        const value = doc.createElement('dd');
        value.textContent = entry.value;
        row.append(term, value);
        list.append(row);
    }
    details.append(summary, list);
    return details;
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
        this.textarea = doc.getElementById('compose-text');
        this.count = doc.getElementById('compose-count');
        this.shareBox = doc.getElementById('compose-share-box');
        this.share = doc.getElementById('compose-share');
        this.shareStatus = doc.getElementById('compose-share-status');
        this.version = -1;
        this.live = new Map();
        this.shown = new Map();
        this.mine = new Set();
        this.cursor = null;
        this.joining = null;
        this.permission = null;
        this.timer = 0;
        this.disposed = false;
        this.onKeyDown = event => this.keyDown(event);
        this.onInput = () => this.recount();
        this.onOlder = () => this.loadOlder();
        this.onReconnected = () => this.join();
        this.onShare = () => this.toggleShare();
        this.onPermission = () => this.permissionChanged();
    }

    async start() {
        this.textarea?.addEventListener('keydown', this.onKeyDown);
        this.textarea?.addEventListener('input', this.onInput);
        this.older?.addEventListener('click', this.onOlder);
        this.win.addEventListener('chaos:reconnected', this.onReconnected);
        this.recount();
        this.timer = this.win.setInterval(() => this.ping(), PING_MS);
        try {
            await this.setupShare();
        } catch {
        }
        try {
            await this.offer();
        } catch {
        }
        await this.join();
    }

    geolocation() {
        return this.win.navigator?.geolocation ?? null;
    }

    async setupShare() {
        if (!this.share || !this.shareBox || !this.geolocation() || this.disposed) {
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
        this.share.checked = shareDefault(readShare(this.win.localStorage), state);
        this.share.addEventListener('change', this.onShare);
        this.shareBox.hidden = false;
    }

    setShareStatus(text) {
        if (this.shareStatus) {
            this.shareStatus.textContent = text;
        }
    }

    permissionChanged() {
        if (this.permission?.state === 'denied' && this.share?.checked) {
            this.share.checked = false;
            this.setShareStatus('Location permission was turned off, so nothing is attached.');
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

    async toggleShare() {
        if (!this.share) {
            return;
        }
        if (!this.share.checked) {
            writeShare(this.win.localStorage, 'off');
            this.setShareStatus('Nothing is attached to your messages.');
            return;
        }
        this.share.disabled = true;
        this.setShareStatus('Asking for your location.');
        try {
            await this.position();
            writeShare(this.win.localStorage, 'on');
            this.setShareStatus('Your location and browser details will be attached to your messages.');
        } catch {
            this.share.checked = false;
            this.setShareStatus('No location permission, so nothing is attached.');
        } finally {
            this.share.disabled = false;
        }
    }

    async meta() {
        if (this.disposed || !this.share?.checked) {
            return null;
        }
        try {
            return collectMeta(await this.position(), this.win);
        } catch (error) {
            if (error?.code === 1) {
                this.share.checked = false;
            }
            this.setShareStatus('Could not read your location, so that message has no details.');
            return null;
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
        this.textarea.dispatchEvent(new this.win.Event('change', { bubbles: true }));
        this.textarea.form?.requestSubmit();
    }

    async offer() {
        const local = await this.history.newest(OFFER_LIMIT * 2);
        for (const record of local) {
            if (record.mine) {
                this.mine.add(record.id);
            }
        }
        for (const chunk of chunkOffers(local)) {
            if (this.disposed) {
                return;
            }
            await this.dotnet.invokeMethodAsync(
                'Offer',
                chunk.map(message => message.id),
                chunk.map(message => message.text),
                chunk.map(message => (Array.isArray(message.meta) && message.meta.length > 0 ? message.meta : null)));
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

    async save(messages) {
        try {
            await this.history.save(messages.map(message => toRecord(message, this.mine.has(message.id))));
        } catch {
        }
    }

    async snapshot(update) {
        this.version = update.version;
        this.live = new Map(update.added.map(message => [message.id, message]));
        await this.save(update.added);
        let archive = { items: [], last: null, done: true };
        try {
            archive = await this.history.page(null, PAGE_SIZE, this.live);
        } catch {
        }
        this.cursor = archive.last;
        this.remember(archive.items);
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
        await this.save(update.added);
    }

    async sent(id) {
        if (this.textarea && !this.disposed) {
            this.textarea.value = '';
        }
        this.recount();
        await this.own(id);
    }

    async own(id) {
        if (this.disposed || typeof id !== 'string') {
            return;
        }
        this.mine.add(id);
        const existing = this.shown.get(id);
        const message = this.live.get(id);
        if (existing && message) {
            const element = this.item(message, existing.classList.contains('fresh'));
            existing.replaceWith(element);
            this.shown.set(id, element);
        }
        if (message) {
            await this.save([message]);
        }
    }

    remember(records) {
        for (const record of records) {
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
            const archive = await this.history.page(this.cursor, PAGE_SIZE, this.live);
            this.cursor = archive.last ?? this.cursor;
            this.remember(archive.items);
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
        const mine = this.mine.has(message.id) || message.mine === true;
        const element = this.doc.createElement('li');
        element.className = 'msg';
        element.classList.toggle('archived', !this.live.has(message.id));
        element.classList.toggle('fresh', fresh);
        element.classList.toggle('mine', mine);
        element.dataset.id = message.id;
        const head = this.doc.createElement('div');
        head.className = 'head';
        const time = this.doc.createElement('time');
        time.dateTime = isoOf(message.id);
        time.textContent = labelOf(message.id);
        head.append(time);
        if (mine) {
            const yours = this.doc.createElement('span');
            yours.className = 'yours';
            yours.textContent = 'Your message';
            head.append(yours);
        }
        const text = this.doc.createElement('p');
        text.className = 'text';
        text.textContent = message.text;
        element.append(head, text);
        const details = metaElement(this.doc, message.meta);
        if (details) {
            element.append(details);
        }
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
        if (this.disposed || !this.feed?.isConnected || messages.length === 0) {
            return;
        }
        const newest = [...messages].sort(newestFirst)[0];
        announce(this.doc, `New message: ${newest.text.slice(0, 140)}`);
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
        this.textarea?.removeEventListener('input', this.onInput);
        this.older?.removeEventListener('click', this.onOlder);
        this.share?.removeEventListener('change', this.onShare);
        this.permission?.removeEventListener('change', this.onPermission);
        this.win.removeEventListener('chaos:reconnected', this.onReconnected);
    }
}

export function attach(dotnet) {
    const client = new BoardClient(dotnet, document, window);
    client.start();
    return {
        apply: updates => client.apply(updates),
        meta: () => client.meta(),
        sent: id => client.sent(id),
        dispose: () => client.dispose(),
    };
}
