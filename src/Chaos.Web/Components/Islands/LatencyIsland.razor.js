import { LocalHistory, OFFER_LIMIT, PAGE_SIZE, announce, chunkOffers, isoOf, labelOf, needsResync, newestFirst } from './BoardIsland.razor.js';

export const DB_NAME = 'chaos-latency';
export const DB_VERSION = 1;
export const STORE = 'runs';
export const LOCAL_CAP = 10000;
export const PING_MS = 25000;
export const RUNS_KEY = 'chaos-latency-runs';
export const NEXT_KEY = 'chaos-latency-next';
export const AUTO_UNLOCK = 10;
export const SAMPLES = 3;
export const PARALLEL = 6;
export const REQUEST_MS = 4000;
export const MAX_MS = 60000;

export const migrations = [
    db => db.createObjectStore(STORE, { keyPath: 'id' }),
];

export const SCHEMA = Object.freeze({ name: DB_NAME, version: DB_VERSION, store: STORE, migrations, cap: LOCAL_CAP });

const KEY = /^[a-z0-9-]{1,32}$/;
const VALUE = /^(0|[1-9][0-9]{0,4})?$/;

function value(text) {
    if (!VALUE.test(text)) {
        return undefined;
    }
    if (text === '') {
        return null;
    }
    const number = Number.parseInt(text, 10);
    return number <= MAX_MS ? number : undefined;
}

export function parseRun(text) {
    if (typeof text !== 'string' || text.length === 0 || text.length > 4096) {
        return null;
    }
    const entries = text.split(';');
    if (entries.length > 64) {
        return null;
    }
    const keys = new Set();
    const results = [];
    for (const entry of entries) {
        const equals = entry.indexOf('=');
        if (equals <= 0) {
            return null;
        }
        const key = entry.slice(0, equals);
        const values = entry.slice(equals + 1).split(',');
        if (!KEY.test(key) || keys.has(key) || values.length !== 2) {
            return null;
        }
        const browser = value(values[0]);
        const server = value(values[1]);
        if (browser === undefined || server === undefined) {
            return null;
        }
        keys.add(key);
        results.push({ key, browser, server });
    }
    return results;
}

export function median(values) {
    const sorted = values.filter(item => Number.isInteger(item)).sort((a, b) => a - b);
    if (sorted.length === 0) {
        return null;
    }
    const middle = Math.floor(sorted.length / 2);
    return sorted.length % 2 === 1 ? sorted[middle] : Math.round((sorted[middle - 1] + sorted[middle]) / 2);
}

export function summarize(results) {
    return {
        browser: median(results.map(result => result.browser)),
        server: median(results.map(result => result.server)),
        answered: results.filter(result => result.browser !== null || result.server !== null).length,
        total: results.length,
    };
}

export function ms(value) {
    return Number.isInteger(value) ? `${value} ms` : '–';
}

export function parseStartReply(reply) {
    if (reply === 'busy') {
        return { kind: 'busy' };
    }
    const parts = typeof reply === 'string' ? reply.split(' ') : [];
    if (parts[0] === 'wait' && parts.length === 2) {
        const seconds = Number.parseInt(parts[1], 10);
        return Number.isInteger(seconds) && seconds > 0 ? { kind: 'wait', seconds } : { kind: 'error' };
    }
    if (parts[0] === 'ok' && parts.length === 3 && /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(parts[1])) {
        const seconds = Number.parseInt(parts[2], 10);
        return { kind: 'ok', id: parts[1], seconds: Number.isInteger(seconds) && seconds > 0 ? seconds : 300 };
    }
    return { kind: 'error' };
}

export function clockText(milliseconds) {
    const total = Math.max(0, Math.ceil(milliseconds / 1000));
    return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
}

export function progressText(runs, unlock = AUTO_UNLOCK) {
    if (runs >= unlock) {
        return `${runs} tests from this browser.`;
    }
    const left = unlock - runs;
    return `${left} more ${left === 1 ? 'test unlocks' : 'tests unlock'} auto run.`;
}

export function readNumber(storage, key) {
    try {
        const parsed = Number.parseInt(storage?.getItem(key) ?? '0', 10);
        return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
    } catch {
        return 0;
    }
}

function writeNumber(storage, key, number) {
    try {
        storage?.setItem(key, String(number));
    } catch {
    }
}

export function fetchOptions(url, signal) {
    const options = { cache: 'no-store', credentials: 'omit', referrerPolicy: 'no-referrer', redirect: 'follow', signal };
    if (/^https?:\/\//.test(url)) {
        options.mode = 'no-cors';
    }
    return options;
}

export function cacheBusted(url, nonce) {
    if (/^https?:\/\//.test(url)) {
        return url;
    }
    return `${url}${url.includes('?') ? '&' : '?'}n=${nonce}`;
}

export function defaults(win) {
    return {
        fetch: (url, options) => win.fetch(url, options),
        now: () => win.performance.now(),
        setTimeout: (callback, delay) => win.setTimeout(callback, delay),
        clearTimeout: handle => win.clearTimeout(handle),
        AbortController: win.AbortController,
    };
}

export async function timeOnce(url, deps, parent, nonce = 0) {
    const controller = new deps.AbortController();
    const onAbort = () => controller.abort();
    parent?.addEventListener('abort', onAbort);
    const timer = deps.setTimeout(() => controller.abort(), REQUEST_MS);
    try {
        if (parent?.aborted) {
            return null;
        }
        const start = deps.now();
        await deps.fetch(cacheBusted(url, nonce), fetchOptions(url, controller.signal));
        return Math.max(0, Math.round(deps.now() - start));
    } catch {
        return null;
    } finally {
        deps.clearTimeout(timer);
        parent?.removeEventListener('abort', onAbort);
    }
}

export async function measure(url, deps, parent, samples = SAMPLES) {
    let nonce = 0;
    if (await timeOnce(url, deps, parent, nonce++) === null) {
        return null;
    }
    let best = null;
    for (let index = 0; index < samples; index += 1) {
        const elapsed = await timeOnce(url, deps, parent, nonce++);
        if (elapsed === null) {
            break;
        }
        best = best === null ? elapsed : Math.min(best, elapsed);
    }
    return best === null ? null : Math.min(best, MAX_MS);
}

export async function measureAll(urls, deps, budgetMs, onResult = () => {}, parallel = PARALLEL) {
    const results = urls.map(() => null);
    const controller = new deps.AbortController();
    const deadline = deps.setTimeout(() => controller.abort(), budgetMs);
    let next = 0;
    const worker = async () => {
        while (next < urls.length && !controller.signal.aborted) {
            const index = next;
            next += 1;
            const result = await measure(urls[index], deps, controller.signal);
            results[index] = controller.signal.aborted ? null : result;
            onResult(index, results[index]);
        }
    };
    try {
        await Promise.all(Array.from({ length: Math.min(parallel, urls.length) }, worker));
    } finally {
        deps.clearTimeout(deadline);
    }
    return results;
}

class LatencyClient {
    constructor(panel, dotnet, doc, win) {
        this.panel = panel;
        this.dotnet = dotnet;
        this.doc = doc;
        this.win = win;
        this.history = new LocalHistory(win.indexedDB, SCHEMA);
        this.rows = [...panel.querySelectorAll('tbody tr')];
        this.endpoints = this.rows.map(row => ({
            key: row.dataset.key,
            url: row.dataset.url,
            label: row.dataset.label,
            server: row.dataset.server === 'true',
            browserCell: row.querySelector('.browser'),
            serverCell: row.querySelector('.server'),
        }));
        this.labels = new Map(this.endpoints.map(endpoint => [endpoint.key, endpoint.label]));
        this.runButton = panel.querySelector('.run');
        this.autoButton = panel.querySelector('.auto');
        this.status = panel.querySelector('.status');
        this.progress = panel.querySelector('.progress');
        this.feed = doc.getElementById('latency-feed');
        this.empty = doc.getElementById('latency-empty');
        this.older = doc.getElementById('latency-older');
        this.cooldown = (Number.parseInt(panel.dataset.cooldown, 10) || 300) * 1000;
        this.budget = (Number.parseInt(panel.dataset.budget, 10) || 30) * 1000;
        this.runs = readNumber(win.localStorage, RUNS_KEY);
        this.next = readNumber(win.localStorage, NEXT_KEY);
        this.retryAt = 0;
        this.version = -1;
        this.live = new Map();
        this.shown = new Map();
        this.mine = new Set();
        this.cursor = null;
        this.ready = false;
        this.running = false;
        this.testing = false;
        this.waiting = null;
        this.auto = false;
        this.joining = null;
        this.timer = 0;
        this.ticker = 0;
        this.disposed = false;
        this.onRun = () => this.press(true);
        this.onAuto = () => this.toggleAuto();
        this.onOlder = () => this.loadOlder();
        this.onReconnected = () => this.join();
    }

    async start() {
        this.runButton?.addEventListener('click', this.onRun);
        this.autoButton?.addEventListener('click', this.onAuto);
        this.older?.addEventListener('click', this.onOlder);
        this.win.addEventListener('chaos:reconnected', this.onReconnected);
        this.timer = this.win.setInterval(() => this.ping(), PING_MS);
        this.ticker = this.win.setInterval(() => this.tick(), 1000);
        this.updateControls();
        try {
            await this.offer();
        } catch {
        }
        await this.join();
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
            await this.dotnet.invokeMethodAsync('Offer', chunk.map(record => record.id), chunk.map(record => record.text));
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
            this.running = update.running === true;
            if (update.kind === 'snapshot') {
                await this.snapshot(update);
            } else if (update.kind === 'delta' && this.version >= 0) {
                if (needsResync(this.version, update)) {
                    this.join();
                    return;
                }
                await this.delta(update);
            }
            this.updateControls();
        }
    }

    record(message) {
        return { id: message.id, text: message.text, mine: this.mine.has(message.id) };
    }

    async save(messages) {
        try {
            await this.history.save(messages.map(message => this.record(message)));
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
        this.render([...update.added, ...archive.items].sort(newestFirst), archive.done);
        for (const message of update.added) {
            this.landed(message);
        }
        if (!this.ready) {
            this.ready = true;
            this.panel.dataset.ready = '';
        }
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
            this.landed(message);
        }
        this.updateEmpty();
        if (fresh.length > 0 && !this.disposed && this.feed?.isConnected) {
            const newest = [...fresh].sort(newestFirst)[0];
            const summary = summarize(parseRun(newest.text) ?? []);
            announce(this.doc, `New latency result: browser median ${ms(summary.browser)}, server median ${ms(summary.server)}.`);
        }
        await this.save(update.added);
    }

    landed(message) {
        if (message.id !== this.waiting) {
            return;
        }
        this.waiting = null;
        const results = new Map((parseRun(message.text) ?? []).map(result => [result.key, result]));
        for (const endpoint of this.endpoints) {
            const result = results.get(endpoint.key);
            if (endpoint.serverCell) {
                endpoint.serverCell.textContent = ms(result?.server ?? null);
            }
        }
        this.setStatus('Done. Your result is at the top of the list.');
    }

    clearTable() {
        for (const endpoint of this.endpoints) {
            if (endpoint.browserCell) {
                endpoint.browserCell.textContent = '…';
            }
            if (endpoint.serverCell) {
                endpoint.serverCell.textContent = endpoint.server ? '…' : '–';
            }
        }
    }

    canPress(now = Date.now()) {
        return this.ready && !this.testing && !this.running && !this.disposed && now >= this.next;
    }

    async press(manual) {
        const now = Date.now();
        if (!this.ready || this.testing || this.disposed) {
            return;
        }
        if (now < this.next) {
            if (manual) {
                this.setStatus(`You can run the next test in ${clockText(this.next - now)}.`);
            }
            return;
        }
        if (this.running) {
            if (manual) {
                this.setStatus('Someone else is testing right now. Try again in a few seconds.');
            }
            return;
        }
        this.testing = true;
        this.updateControls();
        let reply = { kind: 'error' };
        try {
            reply = parseStartReply(await this.dotnet.invokeMethodAsync('Begin'));
        } catch {
        }
        if (this.disposed) {
            return;
        }
        if (reply.kind === 'ok') {
            await this.test(reply);
        } else if (reply.kind === 'busy') {
            this.retryAt = Date.now() + 10000 + Math.round(Math.random() * 20000);
            this.setStatus(manual ? 'Someone else is testing right now. Try again in a few seconds.' : '');
        } else if (reply.kind === 'wait') {
            this.setNext(Date.now() + reply.seconds * 1000);
            this.setStatus(`A test ran from your network recently. Devices sharing an internet connection share this limit. The next one is possible in ${clockText(reply.seconds * 1000)}.`);
        } else {
            this.setStatus('Could not reach the server.');
        }
        this.testing = false;
        this.updateControls();
    }

    async test(reply) {
        this.mine.add(reply.id);
        this.waiting = reply.id;
        this.runs += 1;
        writeNumber(this.win.localStorage, RUNS_KEY, this.runs);
        this.setNext(Date.now() + reply.seconds * 1000);
        this.clearTable();
        let done = 0;
        this.setStatus(`Testing from your browser: 0 of ${this.endpoints.length}.`);
        const results = await measureAll(this.endpoints.map(endpoint => endpoint.url), defaults(this.win), this.budget, (index, result) => {
            done += 1;
            const cell = this.endpoints[index].browserCell;
            if (cell) {
                cell.textContent = ms(result);
            }
            this.setStatus(`Testing from your browser: ${done} of ${this.endpoints.length}.`);
        });
        if (this.disposed) {
            return;
        }
        let accepted = false;
        try {
            accepted = await this.dotnet.invokeMethodAsync('Finish', reply.id, results.map(result => (result === null ? -1 : result)));
        } catch {
        }
        if (this.waiting === reply.id) {
            this.setStatus(accepted ? 'Your browser is done. Waiting for the server.' : 'The server stopped waiting for this browser. Its own numbers still count.');
        }
    }

    setNext(at) {
        this.next = at;
        writeNumber(this.win.localStorage, NEXT_KEY, at);
    }

    toggleAuto() {
        if (this.runs < AUTO_UNLOCK) {
            return;
        }
        this.auto = !this.auto;
        this.setStatus(this.auto ? 'Auto run is on. It runs a test whenever one is allowed.' : 'Auto run is off.');
        this.updateControls();
    }

    tick() {
        if (this.disposed) {
            return;
        }
        const now = Date.now();
        if (this.auto && this.canPress(now) && now >= this.retryAt) {
            this.press(false);
            return;
        }
        this.updateControls();
    }

    updateControls() {
        const now = Date.now();
        if (this.runButton) {
            this.runButton.disabled = !this.ready || this.testing || this.running || now < this.next;
            this.runButton.textContent = this.testing ? 'Testing…' : now < this.next ? `Next test in ${clockText(this.next - now)}` : 'Run test';
        }
        if (this.autoButton) {
            this.autoButton.hidden = this.runs < AUTO_UNLOCK;
            this.autoButton.setAttribute('aria-pressed', String(this.auto));
        }
        if (this.progress) {
            this.progress.textContent = this.running && !this.testing && !this.waiting
                ? 'Someone is running a test right now.'
                : progressText(this.runs);
        }
    }

    setStatus(text) {
        if (this.status) {
            this.status.textContent = text;
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
            for (const record of archive.items) {
                if (record.mine) {
                    this.mine.add(record.id);
                }
                if (!this.shown.has(record.id)) {
                    this.insert(record, false);
                }
            }
            this.older.hidden = archive.done;
        } catch {
            this.older.hidden = true;
        } finally {
            this.older.disabled = false;
        }
    }

    element(tag, className, text) {
        const element = this.doc.createElement(tag);
        if (className) {
            element.className = className;
        }
        if (text !== undefined) {
            element.textContent = text;
        }
        return element;
    }

    item(message, fresh) {
        const results = parseRun(message.text) ?? [];
        const summary = summarize(results);
        const mine = this.mine.has(message.id) || message.mine === true;
        const element = this.element('li', 'msg');
        element.classList.toggle('mine', mine);
        element.classList.toggle('archived', !this.live.has(message.id));
        element.classList.toggle('fresh', fresh);
        element.dataset.id = message.id;
        const time = this.element('time', null, labelOf(message.id));
        time.dateTime = isoOf(message.id);
        const line = this.element('p', 'summary');
        for (const [label, value] of [['Browser median ', ms(summary.browser)], ['Server median ', ms(summary.server)]]) {
            const span = this.element('span', null, label);
            span.append(this.element('strong', null, value));
            line.append(span);
        }
        line.append(this.element('span', null, `${summary.answered} of ${summary.total} answered`));
        element.append(time, line);
        if (mine) {
            const tags = this.element('p', 'tags');
            tags.append(this.element('span', 'yours', 'Your test'));
            element.append(tags);
        }
        const details = this.element('details');
        details.append(this.element('summary', null, 'All endpoints'));
        const wrap = this.element('div', 'table-wrap');
        const table = this.element('table');
        const head = this.element('thead');
        const headRow = this.element('tr');
        for (const title of ['Endpoint', 'Browser', 'Server']) {
            const cell = this.element('th', null, title);
            cell.scope = 'col';
            headRow.append(cell);
        }
        head.append(headRow);
        const body = this.element('tbody');
        for (const result of results) {
            const row = this.element('tr');
            const name = this.element('th', null, this.labels.get(result.key) ?? result.key);
            name.scope = 'row';
            row.append(name, this.element('td', null, ms(result.browser)), this.element('td', null, ms(result.server)));
            body.append(row);
        }
        table.append(head, body);
        wrap.append(table);
        details.append(wrap);
        element.append(details);
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
            if (message.mine) {
                this.mine.add(message.id);
            }
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

    updateEmpty() {
        if (this.empty) {
            this.empty.hidden = this.shown.size > 0;
        }
    }

    dispose() {
        this.disposed = true;
        this.win.clearInterval(this.timer);
        this.win.clearInterval(this.ticker);
        this.runButton?.removeEventListener('click', this.onRun);
        this.autoButton?.removeEventListener('click', this.onAuto);
        this.older?.removeEventListener('click', this.onOlder);
        this.win.removeEventListener('chaos:reconnected', this.onReconnected);
    }
}

export function attach(panel, dotnet) {
    const client = new LatencyClient(panel, dotnet, document, window);
    client.start();
    return {
        apply: updates => client.apply(updates),
        dispose: () => client.dispose(),
    };
}
