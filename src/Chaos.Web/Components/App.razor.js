export const THEME_KEY = 'chaos-theme';
export const MAX_RETRIES = 30;

export function storedTheme(storage) {
    try {
        const value = storage?.getItem(THEME_KEY);
        return value === 'light' || value === 'dark' ? value : null;
    } catch {
        return null;
    }
}

export function toggledTheme(current, prefersDark) {
    const effective = current ?? (prefersDark ? 'dark' : 'light');
    return effective === 'dark' ? 'light' : 'dark';
}

export function reconnectDelay(attempt, random = Math.random) {
    if (attempt <= 0) {
        return 0;
    }
    const ceiling = Math.min(30000, 1000 * 2 ** (attempt - 1));
    return Math.round(ceiling / 2 + random() * ceiling / 2);
}

export const CLOCK_SYNC_MS = 10 * 60 * 1000;
export const DRIFT_PER_MS = 0.0001;

export function zoneLabel(offsetMinutes) {
    if (!offsetMinutes) {
        return 'UTC';
    }
    const magnitude = Math.abs(offsetMinutes);
    const hours = String(Math.floor(magnitude / 60)).padStart(2, '0');
    const minutes = String(magnitude % 60).padStart(2, '0');
    return `UTC${offsetMinutes < 0 ? '-' : '+'}${hours}:${minutes}`;
}

export function clockLabel(serverMs, offsetMinutes) {
    const iso = new Date(serverMs + offsetMinutes * 60000).toISOString();
    return `${iso.slice(0, 10)} ${iso.slice(11, 19)} ${zoneLabel(offsetMinutes)}`;
}

export function sample(serverMs, start, end, offsetMinutes = 0) {
    const low = Math.min(start, end);
    const high = Math.max(start, end);
    return { offset: serverMs - (low + high) / 2, error: (high - low) / 2, at: high, zone: offsetMinutes };
}

export function uncertainty(current, now) {
    return current ? current.error + Math.max(0, now - current.at) * DRIFT_PER_MS : Infinity;
}

export function better(candidate, current, now) {
    return !current || candidate.error <= uncertainty(current, now);
}

export function untilNextSecond(serverMs) {
    return 1000 - (((Math.floor(serverMs) % 1000) + 1000) % 1000) + 5;
}

export class ServerClock {
    constructor(win) {
        this.win = win;
        this.doc = win.document;
        this.current = null;
        this.timer = 0;
        this.lastPerf = 0;
        this.lastWall = 0;
        this.syncing = null;
        this.syncedAt = -Infinity;
    }

    element() {
        return this.doc.getElementById('server-clock');
    }

    start() {
        const element = this.element();
        const now = Number(element?.dataset.now);
        if (Number.isFinite(now) && now > 0) {
            const navigation = this.win.performance.getEntriesByType?.('navigation')?.[0];
            const start = navigation?.requestStart || 0;
            const end = navigation?.responseStart || this.win.performance.now();
            this.current = sample(now, start, end, Number(element.dataset.offset) || 0);
        }
        this.doc.addEventListener('visibilitychange', () => {
            if (this.doc.visibilityState === 'visible') {
                this.sync(true);
            }
        });
        this.lastPerf = this.win.performance.now();
        this.lastWall = Date.now();
        this.tick();
        this.sync(false);
    }

    async sync(force) {
        const perf = this.win.performance;
        if (this.syncing || (!force && perf.now() - this.syncedAt < CLOCK_SYNC_MS && this.current)) {
            return;
        }
        this.syncing = (async () => {
            try {
                const start = perf.now();
                const response = await this.win.fetch(`/time?n=${Math.round(start)}`, { cache: 'no-store', credentials: 'omit' });
                const end = perf.now();
                if (!response.ok) {
                    return;
                }
                const body = await response.json();
                if (!Number.isFinite(body?.unixMs)) {
                    return;
                }
                const candidate = sample(body.unixMs, start, end, Number(body.offsetMinutes) || 0);
                if (force || better(candidate, this.current, end)) {
                    this.current = candidate;
                } else {
                    this.current = { ...this.current, zone: candidate.zone };
                }
                this.syncedAt = end;
                this.render();
            } catch {
            } finally {
                this.syncing = null;
            }
        })();
        await this.syncing;
    }

    now() {
        return this.current ? this.win.performance.now() + this.current.offset : null;
    }

    render() {
        const element = this.element();
        const now = this.now();
        if (!element || now === null) {
            return now;
        }
        element.textContent = clockLabel(now, this.current.zone);
        element.dateTime = new Date(now).toISOString();
        return now;
    }

    tick() {
        const perf = this.win.performance.now();
        const wall = Date.now();
        if (Math.abs((wall - this.lastWall) - (perf - this.lastPerf)) > 2000) {
            this.sync(true);
        }
        this.lastPerf = perf;
        this.lastWall = wall;
        if (perf - this.syncedAt >= CLOCK_SYNC_MS) {
            this.sync(false);
        }
        const now = this.render();
        this.win.clearTimeout(this.timer);
        this.timer = this.win.setTimeout(() => this.tick(), now === null ? 1000 : untilNextSecond(now));
    }
}

export function boot(win) {
    const doc = win.document;
    const darkQuery = win.matchMedia('(prefers-color-scheme: dark)');

    const applyTheme = theme => {
        if (theme) {
            doc.documentElement.dataset.theme = theme;
        } else {
            delete doc.documentElement.dataset.theme;
        }
        const dark = theme ? theme === 'dark' : darkQuery.matches;
        for (const button of doc.querySelectorAll('[data-theme-toggle]')) {
            button.setAttribute('aria-pressed', String(dark));
        }
    };

    const applyStoredTheme = () => applyTheme(storedTheme(win.localStorage));

    const toggleTheme = () => {
        const next = toggledTheme(doc.documentElement.dataset.theme ?? null, darkQuery.matches);
        try {
            win.localStorage.setItem(THEME_KEY, next);
        } catch {
        }
        applyTheme(next);
    };

    const retryWhenVisible = () => {
        if (doc.visibilityState === 'visible') {
            retry();
        }
    };

    const retry = async () => {
        doc.removeEventListener('visibilitychange', retryWhenVisible);
        try {
            if (await win.Blazor.reconnect()) {
                return;
            }
            if (await win.Blazor.resumeCircuit()) {
                return;
            }
            win.location.reload();
        } catch {
            doc.addEventListener('visibilitychange', retryWhenVisible);
        }
    };

    const resume = async () => {
        try {
            if (await win.Blazor.resumeCircuit()) {
                return;
            }
        } catch {
        }
        win.location.reload();
    };

    const clearHistory = async button => {
        const output = doc.getElementById(button.dataset.clearHistory);
        button.disabled = true;
        try {
            const [
                { DB_NAME: BOARD_DB, SHARE_KEY, deleteDatabase },
                { DB_NAME: SLOTS_DB, PRESS_KEY },
                { DB_NAME: CANVAS_DB },
                { DB_NAME: LATENCY_DB, RUNS_KEY, NEXT_KEY },
                { DB_NAME: POSTCARD_DB },
            ] = await Promise.all([
                import('./Islands/BoardIsland.razor.js'),
                import('./Islands/SlotIsland.razor.js'),
                import('./Islands/CanvasIsland.razor.js'),
                import('./Islands/LatencyIsland.razor.js'),
                import('./Islands/PostcardIsland.razor.js'),
            ]);
            const outcomes = await Promise.all([BOARD_DB, POSTCARD_DB, SLOTS_DB, CANVAS_DB, LATENCY_DB].map(name => deleteDatabase(win.indexedDB, name)));
            for (const key of [SHARE_KEY, PRESS_KEY, RUNS_KEY, NEXT_KEY]) {
                try {
                    win.localStorage.removeItem(key);
                } catch {
                }
            }
            if (output) {
                output.textContent = outcomes.includes('blocked')
                    ? 'Close any other board, postcard, canvas, slots or latency tabs to finish clearing.'
                    : 'Local history cleared.';
            }
        } catch {
            if (output) {
                output.textContent = 'Could not clear the local history.';
            }
        } finally {
            button.disabled = false;
        }
    };

    const actions = {
        themeToggle: toggleTheme,
        reconnectRetry: retry,
        reconnectResume: resume,
        reconnectReload: () => win.location.reload(),
        clearHistory,
    };

    doc.addEventListener('click', event => {
        const button = event.target instanceof win.Element ? event.target.closest('button') : null;
        if (!button) {
            return;
        }
        for (const [name, action] of Object.entries(actions)) {
            if (name in button.dataset) {
                action(button);
                return;
            }
        }
    });

    doc.getElementById('components-reconnect-modal')?.addEventListener('components-reconnect-state-changed', event => {
        const state = event.detail?.state;
        if (state === 'rejected') {
            win.location.reload();
        } else if (state === 'hide') {
            win.dispatchEvent(new win.CustomEvent('chaos:reconnected'));
        } else if (state === 'failed') {
            doc.addEventListener('visibilitychange', retryWhenVisible);
        }
    });

    darkQuery.addEventListener('change', applyStoredTheme);
    applyStoredTheme();

    const clock = new ServerClock(win);
    clock.start();

    const afterNavigation = () => {
        applyStoredTheme();
        const announcer = doc.getElementById('announcer');
        if (announcer) {
            announcer.textContent = '';
        }
        clock.render();
    };

    return win.Blazor.start({
        circuit: {
            reconnectionOptions: {
                maxRetries: MAX_RETRIES,
                retryIntervalMilliseconds: attempt => reconnectDelay(attempt),
            },
        },
    }).then(() => win.Blazor.addEventListener('enhancedload', afterNavigation));
}

if (typeof window !== 'undefined' && window.Blazor) {
    boot(window);
}
