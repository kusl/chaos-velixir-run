import { describe, expect, it } from 'vitest';
import { MAX_RETRIES, THEME_KEY, reconnectDelay, storedTheme, toggledTheme } from '../../src/Chaos.Web/Components/App.razor.js';

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
