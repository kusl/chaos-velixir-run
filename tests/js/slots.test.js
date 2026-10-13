import { describe, expect, it } from 'vitest';
import {
    AUTO_UNLOCK,
    LABELS,
    PRESS_KEY,
    SYMBOLS,
    above,
    autoDelay,
    below,
    classify,
    hits,
    isValid,
    parseReply,
    progressText,
    readPresses,
    spinFrames,
    stopTimes,
    stripFor,
} from '../../src/Chaos.Web/Components/Islands/SlotIsland.razor.js';

describe('symbols', () => {
    it('accepts five digits or capital letters only', () => {
        expect(isValid('A1B2C')).toBe(true);
        expect(isValid('a1b2c')).toBe(false);
        expect(isValid('ABCD')).toBe(false);
        expect(isValid('ABCDEF')).toBe(false);
        expect(isValid(null)).toBe(false);
    });

    it('wraps neighbours around the strip', () => {
        expect(above('Z')).toBe('0');
        expect(below('0')).toBe('Z');
        expect(above('9')).toBe('A');
        expect(SYMBOLS).toHaveLength(36);
    });
});

describe('winners', () => {
    it.each([
        ['7Q7A7', 'three'],
        ['XXXXB', 'four'],
        ['KKKKK', 'five'],
        ['13AKZ', 'up'],
        ['ZKA80', 'down'],
        ['ZK8A0', 'none'],
        ['AABBC', 'none'],
        ['ABCDD', 'none'],
    ])('classifies %s as %s', (text, expected) => {
        expect(classify(text)).toBe(expected);
    });

    it('labels every kind of win', () => {
        expect(Object.keys(LABELS).sort()).toEqual(['down', 'five', 'four', 'three', 'up']);
    });

    it('marks the symbols that won', () => {
        expect(hits('7Q7A7')).toEqual([true, false, true, false, true]);
        expect(hits('ABCDE')).toEqual([true, true, true, true, true]);
        expect(hits('AABBC')).toEqual([false, false, false, false, false]);
    });
});

describe('reels', () => {
    it('stops left to right inside the spin', () => {
        const stops = stopTimes(4000);
        expect(stops).toHaveLength(5);
        expect([...stops].sort((a, b) => a - b)).toEqual(stops);
        expect(stops[4]).toBeLessThan(4000);
        expect(stopTimes(0)).toEqual([0, 0, 0, 0, 0]);
    });

    it('builds a strip that starts on the current face and ends on the target', () => {
        const strip = stripFor('A', 'Q', 4, () => 0);
        expect(strip).toEqual(['R', 'Q', 'P', '0', '0', '0', '0', 'B', 'A', '9']);
    });

    it('moves the strip from its last window to its first', () => {
        const frames = spinFrames(10);
        expect(frames[0].transform).toBe('translateY(-70%)');
        expect(frames.at(-1).transform).toBe('translateY(0%)');
    });
});

describe('presses', () => {
    const storage = value => ({ getItem: key => (key === PRESS_KEY ? value : null) });

    it('reads a stored count', () => {
        expect(readPresses(storage('12'))).toBe(12);
        expect(readPresses(storage('nope'))).toBe(0);
        expect(readPresses(storage(null))).toBe(0);
        expect(readPresses({ getItem() { throw new Error('denied'); } })).toBe(0);
    });

    it('counts down to auto spin', () => {
        expect(progressText(0)).toBe(`${AUTO_UNLOCK} more successful presses unlock auto spin.`);
        expect(progressText(AUTO_UNLOCK - 1)).toBe('1 more successful press unlocks auto spin.');
        expect(progressText(AUTO_UNLOCK)).toContain('successful presses from this browser');
    });

    it('understands server replies', () => {
        expect(parseReply('busy')).toEqual({ kind: 'busy', id: null });
        expect(parseReply('slow')).toEqual({ kind: 'slow', id: null });
        expect(parseReply('0199a000-0000-7000-8000-000000000000')).toEqual({ kind: 'accepted', id: '0199a000-0000-7000-8000-000000000000' });
        expect(parseReply(undefined).kind).toBe('error');
    });

    it('waits a little before an automatic press', () => {
        expect(autoDelay(() => 0)).toBe(200);
        expect(autoDelay(() => 1)).toBe(1000);
    });
});
