import { describe, expect, it } from 'vitest';
import {
    MAX_RETRIES,
    THEME_KEY,
    better,
    clockLabel,
    reconnectDelay,
    sample,
    storedTheme,
    toggledTheme,
    uncertainty,
    untilNextSecond,
    zoneLabel,
} from '../../src/Chaos.Web/Components/App.razor.js';

describe('reconnectDelay', () => {
    it('retries the first attempt at once', () => {
        expect(reconnectDelay(0)).toBe(0);
    });

    it('doubles the ceiling and jitters inside its upper half', () => {
        expect(reconnectDelay(1, () => 0)).toBe(500);
        expect(reconnectDelay(1, () => 1)).toBe(1000);
        expect(reconnectDelay(3, () => 0.5)).toBe(3000);
    });

    it('caps the wait at thirty seconds', () => {
        expect(reconnectDelay(20, () => 1)).toBe(30000);
        expect(reconnectDelay(20, () => 0)).toBe(15000);
    });

    it('keeps trying for more than five minutes', () => {
        let total = 0;
        for (let attempt = 0; attempt < MAX_RETRIES; attempt += 1) {
            total += reconnectDelay(attempt, () => 0);
        }
        expect(total).toBeGreaterThan(5 * 60 * 1000);
    });
});

describe('theme', () => {
    const storage = value => ({ getItem: key => (key === THEME_KEY ? value : null) });

    it('reads only known themes', () => {
        expect(storedTheme(storage('dark'))).toBe('dark');
        expect(storedTheme(storage('light'))).toBe('light');
        expect(storedTheme(storage('purple'))).toBeNull();
        expect(storedTheme(undefined)).toBeNull();
    });

    it('survives storage that throws', () => {
        expect(storedTheme({ getItem() { throw new Error('denied'); } })).toBeNull();
    });

    it('flips the system preference when nothing is stored', () => {
        expect(toggledTheme(null, true)).toBe('light');
        expect(toggledTheme(null, false)).toBe('dark');
    });

    it('flips a stored choice regardless of the system', () => {
        expect(toggledTheme('dark', false)).toBe('light');
        expect(toggledTheme('light', true)).toBe('dark');
    });
});

describe('server clock', () => {
    it('labels the server time in the server zone', () => {
        const at = Date.UTC(2026, 9, 16, 8, 10, 39, 500);
        expect(clockLabel(at, 0)).toBe('2026-10-16 08:10:39 UTC');
        expect(clockLabel(at, 330)).toBe('2026-10-16 13:40:39 UTC+05:30');
        expect(clockLabel(at, -240)).toBe('2026-10-16 04:10:39 UTC-04:00');
    });

    it('formats zones', () => {
        expect(zoneLabel(0)).toBe('UTC');
        expect(zoneLabel(60)).toBe('UTC+01:00');
        expect(zoneLabel(-570)).toBe('UTC-09:30');
    });

    it('takes the middle of the round trip', () => {
        expect(sample(10_000, 100, 300)).toEqual({ offset: 9800, error: 100, at: 300, zone: 0 });
    });

    it('prefers tighter samples but lets old ones age out', () => {
        const old = { offset: 0, error: 10, at: 0, zone: 0 };
        expect(better({ error: 50 }, old, 1000)).toBe(false);
        expect(better({ error: 50 }, old, 1_000_000)).toBe(true);
        expect(better({ error: 50 }, null, 0)).toBe(true);
        expect(uncertainty(null, 0)).toBe(Infinity);
    });

    it('wakes just after the next second', () => {
        expect(untilNextSecond(1000)).toBe(1005);
        expect(untilNextSecond(1999)).toBe(6);
        expect(untilNextSecond(1500.7)).toBe(505);
    });
});
