import { describe, expect, it } from 'vitest';
import {
    LOCAL_CAP,
    OFFER_LIMIT,
    chunkOffers,
    excessOf,
    isoOf,
    labelOf,
    needsResync,
    newestFirst,
    timestampOf,
    upgrade,
} from '../../src/Chaos.Web/Components/Islands/BoardIsland.razor.js';

const OLD_ID = '016f5e66-e800-7000-8000-000000000001';

describe('message ids', () => {
    it('reads the UUIDv7 timestamp', () => {
        expect(timestampOf(OLD_ID)).toBe(Date.UTC(2020, 0, 1));
        expect(isoOf(OLD_ID)).toBe('2020-01-01T00:00:00.000Z');
    });

    it('labels today with the time and other days with the date', () => {
        expect(labelOf(OLD_ID, Date.UTC(2020, 0, 1, 12))).toBe('00:00:00');
        expect(labelOf(OLD_ID, Date.UTC(2026, 9, 12))).toBe('2020-01-01 00:00');
    });

    it('sorts newest first', () => {
        const messages = [{ id: '0199a000-0000-7000-8000-000000000000' }, { id: '0199b000-0000-7000-8000-000000000000' }, { id: OLD_ID }];
        expect(messages.sort(newestFirst).map(message => message.id)).toEqual([
            '0199b000-0000-7000-8000-000000000000',
            '0199a000-0000-7000-8000-000000000000',
            OLD_ID,
        ]);
    });
});

describe('chunkOffers', () => {
    const message = (index, text = 'hi') => ({ id: `0199a000-0000-7000-8000-${String(index).padStart(12, '0')}`, text });

    it('keeps small offers in one chunk and stops at the limit', () => {
        const messages = Array.from({ length: 80 }, (_, index) => message(index));
        const chunks = chunkOffers(messages);
        expect(chunks).toHaveLength(1);
        expect(chunks[0]).toHaveLength(OFFER_LIMIT);
    });

    it('splits by size without reordering', () => {
        const messages = Array.from({ length: 10 }, (_, index) => message(index, 'x'.repeat(400)));
        const chunks = chunkOffers(messages, 1000);
        expect(chunks.every(chunk => chunk.length === 2)).toBe(true);
        expect(chunks.flat()).toEqual(messages);
    });

    it('skips a message that can never fit', () => {
        expect(chunkOffers([message(1, 'x'.repeat(2000)), message(2)], 1000)).toEqual([[message(2)]]);
    });
});

describe('bookkeeping', () => {
    it('asks for a resync when a delta skips a version', () => {
        expect(needsResync(4, { previous: 4 })).toBe(false);
        expect(needsResync(4, { previous: 5 })).toBe(true);
    });

    it('trims only beyond the local cap', () => {
        expect(excessOf(LOCAL_CAP)).toBe(0);
        expect(excessOf(LOCAL_CAP + 7)).toBe(7);
        expect(excessOf(3, 2)).toBe(1);
    });
});

describe('schema migrations', () => {
    it('creates the message store on a fresh database', () => {
        const created = [];
        upgrade({ createObjectStore: (name, options) => created.push([name, options]) }, 0);
        expect(created).toEqual([['messages', { keyPath: 'id' }]]);
    });

    it('runs only the steps after the stored version', () => {
        const ran = [];
        upgrade({}, 1, 3, [() => ran.push(1), () => ran.push(2), () => ran.push(3)]);
        expect(ran).toEqual([2, 3]);
    });
});
