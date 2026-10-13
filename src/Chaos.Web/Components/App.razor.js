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
            const { deleteHistory } = await import('./Islands/BoardIsland.razor.js');
            const outcome = await deleteHistory(win.indexedDB);
            if (output) {
                output.textContent = outcome === 'blocked'
                    ? 'Close any other board tabs to finish clearing.'
                    : 'Local message history cleared.';
            }
        } catch {
            if (output) {
                output.textContent = 'Could not clear the local message history.';
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

    return win.Blazor.start({
        circuit: {
            reconnectionOptions: {
                maxRetries: MAX_RETRIES,
                retryIntervalMilliseconds: attempt => reconnectDelay(attempt),
            },
        },
    }).then(() => win.Blazor.addEventListener('enhancedload', applyStoredTheme));
}

if (typeof window !== 'undefined' && window.Blazor) {
    boot(window);
}
