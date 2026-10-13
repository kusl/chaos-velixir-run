import { LocalHistory, OFFER_LIMIT, PAGE_SIZE, chunkOffers, isoOf, labelOf, needsResync, newestFirst } from './BoardIsland.razor.js';

export const DB_NAME = 'chaos-slots';
export const DB_VERSION = 1;
export const STORE = 'spins';
export const LOCAL_CAP = 10000;
export const PING_MS = 25000;
export const PRESS_KEY = 'chaos-slots-presses';
export const AUTO_UNLOCK = 10;
export const SYMBOLS = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ';
export const LENGTH = 5;
export const SYMBOL_MS = 55;

export const migrations = [
    db => db.createObjectStore(STORE, { keyPath: 'id' }),
];

export const SCHEMA = Object.freeze({ name: DB_NAME, version: DB_VERSION, store: STORE, migrations, cap: LOCAL_CAP });

export const LABELS = Object.freeze({
    three: 'Three of a kind',
    four: 'Four of a kind',
    five: 'Five of a kind',
    up: 'Straight up',
    down: 'Straight down',
});

export function isValid(text) {
    return typeof text === 'string' && /^[0-9A-Z]{5}$/.test(text);
}

function tally(text) {
    const counts = new Map();
    for (const symbol of text) {
        counts.set(symbol, (counts.get(symbol) ?? 0) + 1);
    }
    return counts;
}

function ordered(text, direction) {
    for (let index = 1; index < text.length; index += 1) {
        if ((text.charCodeAt(index) - text.charCodeAt(index - 1)) * direction <= 0) {
            return false;
        }
    }
    return true;
}

export function classify(text) {
    if (!isValid(text)) {
        return 'none';
    }
    const most = Math.max(...tally(text).values());
    if (most >= 5) {
        return 'five';
    }
    if (most === 4) {
        return 'four';
    }
    if (most === 3) {
        return 'three';
    }
    if (ordered(text, 1)) {
        return 'up';
    }
    return ordered(text, -1) ? 'down' : 'none';
}

export function hits(text) {
    const win = classify(text);
    if (win === 'none') {
        return Array(LENGTH).fill(false);
    }
    if (win === 'up' || win === 'down') {
        return Array(LENGTH).fill(true);
    }
    const [top] = [...tally(text)].reduce((best, entry) => (entry[1] > best[1] ? entry : best));
    return [...text].map(symbol => symbol === top);
}

export function above(symbol) {
    return SYMBOLS[(SYMBOLS.indexOf(symbol) + 1) % SYMBOLS.length];
}

export function below(symbol) {
    return SYMBOLS[(SYMBOLS.indexOf(symbol) + SYMBOLS.length - 1) % SYMBOLS.length];
}

export function stopTimes(duration, reels = LENGTH) {
    return Array.from({ length: reels }, (_, index) => Math.round(duration * (0.42 + 0.11 * index)));
}

export function stripFor(current, target, spins, random = Math.random) {
    const filler = Array.from({ length: Math.max(0, spins) }, () => SYMBOLS[Math.floor(random() * SYMBOLS.length)]);
    return [above(target), target, below(target), ...filler, above(current), current, below(current)];
}

export function spinFrames(cells) {
    const step = 100 / cells;
    const start = -(cells - 3) * step;
    return [
        { transform: `translateY(${start}%)`, easing: 'ease-in' },
        { transform: `translateY(${start - step * 0.35}%)`, offset: 0.07, easing: 'cubic-bezier(.32,0,.18,1)' },
        { transform: `translateY(${step * 0.3}%)`, offset: 0.92, easing: 'ease-out' },
        { transform: 'translateY(0%)' },
    ];
}

export function readPresses(storage) {
    try {
        const value = Number.parseInt(storage?.getItem(PRESS_KEY) ?? '0', 10);
        return Number.isFinite(value) && value > 0 ? value : 0;
    } catch {
        return 0;
    }
}

export function autoDelay(random = Math.random) {
    return 200 + Math.round(random() * 800);
}

export function parseReply(reply) {
    if (reply === 'busy' || reply === 'slow') {
        return { kind: reply, id: null };
    }
    if (typeof reply === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(reply)) {
        return { kind: 'accepted', id: reply };
    }
    return { kind: 'error', id: null };
}

export function progressText(presses, unlock = AUTO_UNLOCK) {
    if (presses >= unlock) {
        return `${presses} successful presses from this browser.`;
    }
    const left = unlock - presses;
    return `${left} more successful ${left === 1 ? 'press unlocks' : 'presses unlock'} auto spin.`;
}

class SlotClient {
    constructor(machine, dotnet, doc, win) {
        this.machine = machine;
        this.dotnet = dotnet;
        this.doc = doc;
        this.win = win;
        this.history = new LocalHistory(win.indexedDB, SCHEMA);
        this.reels = [...machine.querySelectorAll('.reel')];
        this.strips = this.reels.map(reel => reel.querySelector('.strip'));
        this.spinButton = machine.querySelector('.spin');
        this.autoButton = machine.querySelector('.auto');
        this.status = machine.querySelector('.status');
        this.verdict = machine.querySelector('.verdict');
        this.progress = machine.querySelector('.progress');
        this.feed = doc.getElementById('slots-feed');
        this.empty = doc.getElementById('slots-empty');
        this.older = doc.getElementById('slots-older');
        this.announcer = doc.getElementById('slots-announcer');
        this.faces = [...(isValid(machine.dataset.initial) ? machine.dataset.initial : 'CHAOS')];
        this.motion = win.matchMedia('(prefers-reduced-motion: reduce)');
        this.presses = readPresses(win.localStorage);
        this.version = -1;
        this.live = new Map();
        this.shown = new Map();
        this.mine = new Set();
        this.cursor = null;
        this.spin = null;
        this.ready = false;
        this.pressing = false;
        this.auto = false;
        this.autoTimer = 0;
        this.joining = null;
        this.timer = 0;
        this.disposed = false;
        this.onSpin = () => this.press(true);
        this.onAuto = () => this.toggleAuto();
        this.onOlder = () => this.loadOlder();
        this.onReconnected = () => this.join();
        this.onVisibility = () => this.scheduleAuto();
    }

    async start() {
        this.spinButton?.addEventListener('click', this.onSpin);
        this.autoButton?.addEventListener('click', this.onAuto);
        this.older?.addEventListener('click', this.onOlder);
        this.win.addEventListener('chaos:reconnected', this.onReconnected);
        this.doc.addEventListener('visibilitychange', this.onVisibility);
        this.timer = this.win.setInterval(() => this.ping(), PING_MS);
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
        const spinning = update.spin && this.live.has(update.spin.id) ? update.spin : null;
        if (this.spin && this.spin.message.id !== spinning?.id) {
            this.finish(true);
        }
        const settled = update.added.filter(message => message.id !== spinning?.id);
        await this.save(settled);
        let archive = { items: [], last: null, done: true };
        try {
            archive = await this.history.page(null, PAGE_SIZE, this.live);
        } catch {
        }
        this.cursor = archive.last;
        this.render([...settled, ...archive.items].sort(newestFirst), archive.done);
        if (spinning && !this.spin) {
            this.begin(this.live.get(spinning.id), spinning.remaining);
        } else if (!this.spin && settled.length > 0) {
            this.show(settled.sort(newestFirst)[0].text);
        }
        if (!this.ready) {
            this.ready = true;
            this.machine.dataset.ready = '';
        }
        this.updateControls();
        this.scheduleAuto();
    }

    async delta(update) {
        this.version = update.version;
        for (const id of update.removed) {
            this.live.delete(id);
            this.shown.get(id)?.classList.add('archived');
        }
        const settled = [];
        let spinning = null;
        for (const message of update.added) {
            this.live.set(message.id, message);
            if (update.spin && message.id === update.spin.id) {
                spinning = message;
                continue;
            }
            const existing = this.shown.get(message.id);
            if (existing) {
                existing.classList.remove('archived');
            } else {
                this.insert(message, true);
            }
            settled.push(message);
        }
        this.updateEmpty();
        if (spinning) {
            this.begin(spinning, update.spin.remaining);
        }
        await this.save(settled);
    }

    reducedMotion() {
        return this.motion.matches || typeof this.strips[0]?.animate !== 'function';
    }

    begin(message, remaining) {
        if (this.spin) {
            this.finish(true);
        }
        this.win.clearTimeout(this.autoTimer);
        this.autoTimer = 0;
        const duration = Math.max(0, Math.round(remaining));
        const spin = { message, timers: [], animations: [] };
        this.spin = spin;
        this.machine.classList.remove('win');
        this.machine.classList.add('spinning');
        this.verdict.textContent = '';
        for (const reel of this.reels) {
            reel.classList.remove('hit', 'stopped');
        }
        const stops = stopTimes(duration);
        const reduced = this.reducedMotion();
        stops.forEach((stop, index) => {
            const target = message.text[index];
            if (!reduced && stop > 0) {
                this.roll(spin, index, target, stop);
            }
            spin.timers.push(this.win.setTimeout(() => this.stop(spin, index, target), stop));
        });
        spin.timers.push(this.win.setTimeout(() => this.finish(false), duration));
        this.updateControls();
    }

    roll(spin, index, target, time) {
        const strip = this.strips[index];
        const reel = this.reels[index];
        const symbols = stripFor(this.faces[index], target, Math.max(4, Math.round(time / SYMBOL_MS)));
        this.fill(strip, symbols);
        spin.animations.push(strip.animate(spinFrames(symbols.length), { duration: time, fill: 'forwards' }));
        reel.classList.add('blur');
        spin.timers.push(this.win.setTimeout(() => reel.classList.remove('blur'), Math.round(time * 0.72)));
    }

    stop(spin, index, target) {
        if (this.spin !== spin) {
            return;
        }
        const reel = this.reels[index];
        reel.classList.remove('blur', 'stopped');
        this.setFace(index, target);
        for (const animation of spin.animations) {
            if (animation.effect?.target === this.strips[index]) {
                animation.cancel();
            }
        }
        void reel.offsetWidth;
        reel.classList.add('stopped');
    }

    finish(instant) {
        const spin = this.spin;
        if (!spin) {
            return;
        }
        this.spin = null;
        for (const timer of spin.timers) {
            this.win.clearTimeout(timer);
        }
        for (const animation of spin.animations) {
            animation.cancel();
        }
        const text = spin.message.text;
        this.show(text);
        this.machine.classList.remove('spinning');
        const win = classify(text);
        const marks = hits(text);
        this.reels.forEach((reel, index) => reel.classList.toggle('hit', marks[index]));
        this.machine.classList.toggle('win', win !== 'none');
        this.verdict.textContent = LABELS[win] ?? '';
        this.land(spin.message, !instant);
        this.updateControls();
        this.scheduleAuto();
    }

    land(message, fresh) {
        if (!this.shown.has(message.id)) {
            this.insert(message, fresh);
        }
        this.updateEmpty();
        this.announce(message);
        this.save([message]);
    }

    show(text) {
        [...text].forEach((symbol, index) => this.setFace(index, symbol));
        for (const reel of this.reels) {
            reel.classList.remove('blur', 'hit');
        }
    }

    setFace(index, symbol) {
        this.faces[index] = symbol;
        this.fill(this.strips[index], [above(symbol), symbol, below(symbol)]);
    }

    fill(strip, symbols) {
        const fragment = this.doc.createDocumentFragment();
        for (const symbol of symbols) {
            const cell = this.doc.createElement('span');
            cell.textContent = symbol;
            fragment.append(cell);
        }
        strip.replaceChildren(fragment);
    }

    async press(manual) {
        if (!this.ready || this.spin || this.pressing || this.disposed) {
            return;
        }
        this.pressing = true;
        this.updateControls();
        let reply = { kind: 'error', id: null };
        try {
            reply = parseReply(await this.dotnet.invokeMethodAsync('Press'));
        } catch {
        }
        this.pressing = false;
        if (this.disposed) {
            return;
        }
        if (reply.kind === 'accepted') {
            this.markMine(reply.id);
            this.presses += 1;
            try {
                this.win.localStorage.setItem(PRESS_KEY, String(this.presses));
            } catch {
            }
            this.setStatus('Your press worked. This spin is yours.');
        } else if (reply.kind === 'busy') {
            this.setStatus(manual ? 'Someone else pressed first. Wait for the reels to stop.' : '');
        } else if (reply.kind === 'slow') {
            this.setStatus('Slow down a little.');
        } else {
            this.setStatus('Could not reach the server.');
        }
        this.updateControls();
        this.scheduleAuto();
    }

    markMine(id) {
        this.mine.add(id);
        const existing = this.shown.get(id);
        const message = this.live.get(id);
        if (existing && message) {
            const element = this.item(message, existing.classList.contains('fresh'));
            existing.replaceWith(element);
            this.shown.set(id, element);
            this.save([message]);
        }
    }

    toggleAuto() {
        if (this.presses < AUTO_UNLOCK) {
            return;
        }
        this.auto = !this.auto;
        this.setStatus(this.auto ? 'Auto spin is on. It pauses while this tab is hidden.' : 'Auto spin is off.');
        this.updateControls();
        this.scheduleAuto();
    }

    scheduleAuto() {
        this.win.clearTimeout(this.autoTimer);
        this.autoTimer = 0;
        if (!this.auto || !this.ready || this.spin || this.pressing || this.disposed || this.doc.visibilityState !== 'visible') {
            return;
        }
        this.autoTimer = this.win.setTimeout(() => {
            this.autoTimer = 0;
            this.press(false);
        }, autoDelay());
    }

    updateControls() {
        if (this.spinButton) {
            this.spinButton.disabled = !this.ready || this.spin !== null || this.pressing;
        }
        if (this.autoButton) {
            this.autoButton.hidden = this.presses < AUTO_UNLOCK;
            this.autoButton.setAttribute('aria-pressed', String(this.auto));
        }
        if (this.progress) {
            this.progress.textContent = progressText(this.presses);
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

    item(message, fresh) {
        const win = classify(message.text);
        const marks = hits(message.text);
        const mine = this.mine.has(message.id) || message.mine === true;
        const element = this.doc.createElement('li');
        element.className = 'msg';
        element.classList.toggle('win', win !== 'none');
        element.classList.toggle('mine', mine);
        element.classList.toggle('archived', !this.live.has(message.id));
        element.classList.toggle('fresh', fresh);
        element.dataset.id = message.id;
        const time = this.doc.createElement('time');
        time.dateTime = isoOf(message.id);
        time.textContent = labelOf(message.id);
        const symbols = this.doc.createElement('p');
        symbols.className = 'symbols';
        [...message.text].forEach((symbol, index) => {
            const cell = this.doc.createElement('span');
            cell.textContent = symbol;
            if (marks[index]) {
                cell.className = 'hit';
            }
            symbols.append(cell);
        });
        element.append(time, symbols);
        if (win !== 'none' || mine) {
            const tags = this.doc.createElement('p');
            tags.className = 'tags';
            if (win !== 'none') {
                const badge = this.doc.createElement('strong');
                badge.className = 'badge';
                badge.textContent = LABELS[win];
                tags.append(badge);
            }
            if (mine) {
                const yours = this.doc.createElement('span');
                yours.className = 'yours';
                yours.textContent = 'Your press';
                tags.append(yours);
            }
            element.append(tags);
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

    announce(message) {
        if (!this.announcer) {
            return;
        }
        const win = classify(message.text);
        const spelled = [...message.text].join(' ');
        this.announcer.textContent = win === 'none' ? `Result: ${spelled}.` : `Winner! ${spelled}. ${LABELS[win]}.`;
    }

    updateEmpty() {
        if (this.empty) {
            this.empty.hidden = this.shown.size > 0;
        }
    }

    dispose() {
        this.disposed = true;
        this.win.clearInterval(this.timer);
        this.win.clearTimeout(this.autoTimer);
        if (this.spin) {
            for (const timer of this.spin.timers) {
                this.win.clearTimeout(timer);
            }
            for (const animation of this.spin.animations) {
                animation.cancel();
            }
            this.spin = null;
        }
        this.spinButton?.removeEventListener('click', this.onSpin);
        this.autoButton?.removeEventListener('click', this.onAuto);
        this.older?.removeEventListener('click', this.onOlder);
        this.win.removeEventListener('chaos:reconnected', this.onReconnected);
        this.doc.removeEventListener('visibilitychange', this.onVisibility);
    }
}

export function attach(machine, dotnet) {
    const client = new SlotClient(machine, dotnet, document, window);
    client.start();
    return {
        apply: updates => client.apply(updates),
        dispose: () => client.dispose(),
    };
}
