import { describe, expect, it } from 'vitest';
import { NOTICE_MS, REPEAT_MS, StatusLine } from '../../src/Chaos.Web/Components/Islands/BoardIsland.razor.js';

function clock() {
    let now = 0;
    let next = 1;
    const pending = new Map();
    return {
        setTimeout(callback, ms) {
            const id = next++;
            pending.set(id, { at: now + ms, callback });
            return id;
        },
        clearTimeout(id) {
            pending.delete(id);
        },
        advance(ms) {
            now += ms;
            for (const [id, timer] of [...pending].sort((a, b) => a[1].at - b[1].at)) {
                if (timer.at <= now && pending.has(id)) {
                    pending.delete(id);
                    timer.callback();
                }
            }
        },
        get size() {
            return pending.size;
        },
    };
}

function line(ms) {
    const element = { textContent: '' };
    const timers = clock();
    return { element, timers, status: new StatusLine(element, timers, ms) };
}

describe('StatusLine', () => {
    it('clears unowned notices after a while', () => {
        const { element, timers, status } = line();
        status.show('Slow down a little.');
        expect(element.textContent).toBe('Slow down a little.');
        timers.advance(NOTICE_MS - 1);
        expect(element.textContent).toBe('Slow down a little.');
        timers.advance(1);
        expect(element.textContent).toBe('');
        expect(status.owner).toBeNull();
    });

    it('keeps an owned notice until its owner is released', () => {
        const { element, timers, status } = line();
        status.show('This spin is yours.', 'a');
        timers.advance(NOTICE_MS * 10);
        expect(element.textContent).toBe('This spin is yours.');
        status.release('b');
        expect(element.textContent).toBe('This spin is yours.');
        status.release('a');
        expect(element.textContent).toBe('');
    });

    it('lets an owned notice expire when asked to', () => {
        const { element, timers, status } = line();
        status.show('This spin is yours.', 'a', 500);
        timers.advance(500);
        expect(element.textContent).toBe('');
    });

    it('clears an owned notice when something else takes over', () => {
        const { element, status } = line();
        status.show('This spin is yours.', 'a');
        status.supersede('a');
        expect(element.textContent).toBe('This spin is yours.');
        status.supersede('b');
        expect(element.textContent).toBe('');
        status.show('Done.', 'c');
        status.supersede(null);
        expect(element.textContent).toBe('');
    });

    it('leaves unowned notices to their timer', () => {
        const { element, status } = line();
        status.show('Auto spin is off.');
        status.supersede('b');
        status.release(null);
        expect(element.textContent).toBe('Auto spin is off.');
    });

    it('rewrites a repeated notice so screen readers hear it again', () => {
        const { element, timers, status } = line();
        status.show('Slow down a little.');
        status.show('Slow down a little.');
        expect(element.textContent).toBe('');
        timers.advance(REPEAT_MS);
        expect(element.textContent).toBe('Slow down a little.');
    });

    it('cancels timers on clear and dispose', () => {
        const { element, timers, status } = line();
        status.show('Could not reach the server.');
        status.clear();
        expect(element.textContent).toBe('');
        expect(timers.size).toBe(0);
        status.show('Could not reach the server.');
        status.dispose();
        expect(timers.size).toBe(0);
    });

    it('treats empty text as a clear', () => {
        const { element, timers, status } = line();
        status.show('This spin is yours.', 'a');
        status.show('');
        expect(element.textContent).toBe('');
        expect(status.owner).toBeNull();
        expect(timers.size).toBe(0);
    });

    it('works without an element', () => {
        const status = new StatusLine(null, clock());
        expect(() => {
            status.show('Hello', 'a');
            status.release('a');
            status.dispose();
        }).not.toThrow();
    });
});
