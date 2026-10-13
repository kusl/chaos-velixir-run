export const RESOLUTION = 4095;
export const BRUSH = 0.006;
export const FLUSH_MS = 50;
export const MAX_BATCH = 256;
export const MAX_STROKE = 2000;
export const MIN_STEP = 3;
export const PALM_SIZE = 40;
export const PEN_GRACE_MS = 1500;
export const PING_MS = 25000;

export const Result = Object.freeze({
    Accepted: 0,
    Stale: 1,
    TooFast: 2,
    Invalid: 3,
    Full: 4,
    TooLong: 5,
});

export function quantize(value) {
    return Math.round(Math.min(1, Math.max(0, value)) * RESOLUTION);
}

export function toPoint(clientX, clientY, rect) {
    return [quantize((clientX - rect.left) / rect.width), quantize((clientY - rect.top) / rect.height)];
}

export function farEnough(points, x, y, minStep = MIN_STEP) {
    const length = points.length;
    if (length < 2) {
        return true;
    }
    return Math.hypot(x - points[length - 2], y - points[length - 1]) >= minStep;
}

export function isPalm(event, lastPenAt, now) {
    if (event.pointerType !== 'touch') {
        return false;
    }
    if (now - lastPenAt < PEN_GRACE_MS) {
        return true;
    }
    return event.width > PALM_SIZE || event.height > PALM_SIZE;
}

export function backingSize(cssSize, ratio) {
    return Math.max(1, Math.round(cssSize * ratio));
}

export function brushWidth(size) {
    return Math.max(1, size * BRUSH);
}

export function unseen(have, offset, points) {
    if (offset > have) {
        return null;
    }
    const skip = (have - offset) * 2;
    return skip >= points.length ? [] : points.slice(skip);
}

export function drawPath(context, size, points, from) {
    if (points.length === 0 || from >= points.length) {
        return;
    }
    const scale = size / RESOLUTION;
    context.beginPath();
    if (points.length === 2) {
        context.arc(points[0] * scale, points[1] * scale, context.lineWidth / 2, 0, Math.PI * 2);
        context.fill();
        return;
    }
    const start = Math.max(0, from - 2);
    context.moveTo(points[start] * scale, points[start + 1] * scale);
    for (let index = start + 2; index < points.length; index += 2) {
        context.lineTo(points[index] * scale, points[index + 1] * scale);
    }
    context.stroke();
}

class CanvasClient {
    constructor(canvas, dotnet, win) {
        this.canvas = canvas;
        this.dotnet = dotnet;
        this.win = win;
        this.context = canvas.getContext('2d');
        this.epoch = 0;
        this.size = 0;
        this.strokes = new Map();
        this.localId = 0;
        this.active = null;
        this.queue = [];
        this.sending = false;
        this.flushTimer = 0;
        this.lastPenAt = -Infinity;
        this.joining = null;
        this.timer = 0;
        this.observer = null;
        this.ratioQuery = null;
        this.disposed = false;
        this.handlers = {
            pointerdown: event => this.down(event),
            pointermove: event => this.move(event),
            pointerup: event => this.up(event),
            pointercancel: event => this.up(event),
            lostpointercapture: event => this.up(event),
            contextmenu: event => event.preventDefault(),
            contextrestored: () => this.redraw(),
        };
        this.onRatio = () => {
            this.watchRatio();
            this.resize();
        };
        this.onReconnected = () => this.join();
    }

    start() {
        for (const [name, handler] of Object.entries(this.handlers)) {
            this.canvas.addEventListener(name, handler);
        }
        this.observer = new this.win.ResizeObserver(() => this.resize());
        this.observer.observe(this.canvas);
        this.watchRatio();
        this.win.addEventListener('chaos:reconnected', this.onReconnected);
        this.timer = this.win.setInterval(() => this.ping(), PING_MS);
        this.resize();
        this.join();
    }

    watchRatio() {
        this.ratioQuery?.removeEventListener('change', this.onRatio);
        this.ratioQuery = this.win.matchMedia(`(resolution: ${this.win.devicePixelRatio || 1}dppx)`);
        this.ratioQuery.addEventListener('change', this.onRatio);
    }

    resize() {
        const width = this.canvas.getBoundingClientRect().width;
        if (width === 0) {
            return;
        }
        const size = backingSize(width, this.win.devicePixelRatio || 1);
        if (size === this.size) {
            return;
        }
        this.size = size;
        this.canvas.width = size;
        this.canvas.height = size;
        this.redraw();
    }

    prepare() {
        this.context.lineWidth = brushWidth(this.size);
        this.context.lineCap = 'round';
        this.context.lineJoin = 'round';
        this.context.strokeStyle = '#000';
        this.context.fillStyle = '#000';
    }

    redraw() {
        this.prepare();
        this.context.clearRect(0, 0, this.canvas.width, this.canvas.height);
        for (const points of this.strokes.values()) {
            drawPath(this.context, this.size, points, 0);
        }
    }

    join() {
        if (this.disposed) {
            return Promise.resolve();
        }
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

    apply(updates) {
        for (const update of updates) {
            if (update.kind === 'snapshot') {
                this.snapshot(update);
            } else if (update.kind === 'reset') {
                this.clear(update.epoch);
            } else if (update.kind === 'stroke') {
                this.remote(update);
            }
        }
    }

    snapshot(update) {
        this.clear(update.epoch);
        for (const stroke of update.strokes ?? []) {
            this.strokes.set(`r${stroke.id}`, [...stroke.points]);
        }
        this.redraw();
    }

    clear(epoch) {
        this.epoch = epoch;
        this.strokes.clear();
        this.active = null;
        this.queue = [];
        this.redraw();
    }

    remote(update) {
        if (update.epoch !== this.epoch) {
            return;
        }
        const key = `r${update.stroke}`;
        let points = this.strokes.get(key);
        if (!points) {
            if (update.offset !== 0) {
                this.join();
                return;
            }
            points = [];
            this.strokes.set(key, points);
        }
        const fresh = unseen(points.length / 2, update.offset, update.points);
        if (fresh === null) {
            this.join();
            return;
        }
        if (fresh.length === 0) {
            return;
        }
        const from = points.length;
        points.push(...fresh);
        drawPath(this.context, this.size, points, from);
    }

    down(event) {
        if (this.active || this.epoch === 0 || !event.isPrimary) {
            return;
        }
        if (event.pointerType === 'mouse' && event.button !== 0) {
            return;
        }
        if (event.pointerType === 'pen') {
            this.lastPenAt = event.timeStamp;
        }
        if (isPalm(event, this.lastPenAt, event.timeStamp)) {
            return;
        }
        event.preventDefault();
        this.canvas.setPointerCapture(event.pointerId);
        this.localId += 1;
        const points = [];
        this.strokes.set(`l${this.localId}`, points);
        this.active = { pointerId: event.pointerId, id: this.localId, epoch: this.epoch, points, sent: 0, ended: false };
        this.queue.push(this.active);
        this.add(event);
    }

    move(event) {
        if (!this.active || event.pointerId !== this.active.pointerId) {
            return;
        }
        const samples = typeof event.getCoalescedEvents === 'function' ? event.getCoalescedEvents() : [];
        for (const sample of samples.length > 0 ? samples : [event]) {
            if (!this.active) {
                return;
            }
            this.add(sample);
        }
    }

    up(event) {
        if (this.active && event.pointerId === this.active.pointerId) {
            this.finish();
        }
    }

    add(event) {
        const record = this.active;
        const [x, y] = toPoint(event.clientX, event.clientY, this.canvas.getBoundingClientRect());
        if (!farEnough(record.points, x, y)) {
            return;
        }
        if (record.points.length / 2 >= MAX_STROKE) {
            this.finish();
            return;
        }
        const from = record.points.length;
        record.points.push(x, y);
        drawPath(this.context, this.size, record.points, from);
        this.schedule();
    }

    finish() {
        if (!this.active) {
            return;
        }
        this.active.ended = true;
        this.active = null;
        this.flush();
    }

    schedule() {
        if (this.flushTimer) {
            return;
        }
        this.flushTimer = this.win.setTimeout(() => {
            this.flushTimer = 0;
            this.flush();
        }, FLUSH_MS);
    }

    async flush() {
        if (this.sending || this.disposed) {
            return;
        }
        const record = this.queue[0];
        if (!record) {
            return;
        }
        const total = record.points.length / 2;
        const count = Math.min(MAX_BATCH, total - record.sent);
        const end = record.ended && record.sent + count === total;
        if (count === 0 && !end) {
            return;
        }
        const points = record.points.slice(record.sent * 2, (record.sent + count) * 2);
        this.sending = true;
        let result = Result.Invalid;
        try {
            result = await this.dotnet.invokeMethodAsync('Draw', record.id, record.epoch, points, end);
        } catch {
            result = Result.Invalid;
        } finally {
            this.sending = false;
        }
        if (result === Result.Accepted) {
            record.sent += count;
            if (end) {
                this.drop(record);
            }
        } else {
            this.drop(record);
            if (this.active === record) {
                this.active = null;
            }
            if (result !== Result.Stale) {
                this.join();
            }
        }
        if (this.queue.length > 0) {
            this.schedule();
        }
    }

    drop(record) {
        const index = this.queue.indexOf(record);
        if (index >= 0) {
            this.queue.splice(index, 1);
        }
    }

    dispose() {
        this.disposed = true;
        for (const [name, handler] of Object.entries(this.handlers)) {
            this.canvas.removeEventListener(name, handler);
        }
        this.observer?.disconnect();
        this.ratioQuery?.removeEventListener('change', this.onRatio);
        this.win.removeEventListener('chaos:reconnected', this.onReconnected);
        this.win.clearInterval(this.timer);
        this.win.clearTimeout(this.flushTimer);
    }
}

export function attach(canvas, dotnet) {
    const client = new CanvasClient(canvas, dotnet, window);
    return {
        start: () => client.start(),
        apply: updates => client.apply(updates),
        dispose: () => client.dispose(),
    };
}
