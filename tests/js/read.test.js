import { describe, expect, it } from 'vitest';
import { upgrade } from '../../src/Chaos.Web/Components/Islands/BoardIsland.razor.js';
import {
    DB_NAME,
    DB_VERSION,
    LOCAL_CAP,
    STORE,
    entryOf,
    hrefOf,
    isId,
    migrations,
    overflow,
    readLabel,
    readsText,
    recentFirst,
    remember,
    shelved,
    visit,
} from '../../src/Chaos.Web/Components/Pages/ReadPage.razor.js';

const ID = '0123456789ab';

describe('reading history schema', () => {
    it('creates one store keyed by id', () => {
        const created = [];
        upgrade({ createObjectStore: (name, options) => created.push([name, options]) }, 0, DB_VERSION, migrations);
        expect(DB_NAME).toBe('chaos-reading');
        expect(created).toEqual([[STORE, { keyPath: 'id' }]]);
        expect(LOCAL_CAP).toBe(1000);
    });
});

describe('entries', () => {
    it('accepts twelve lowercase hex digits only', () => {
        expect(isId(ID)).toBe(true);
        expect(isId('0123456789AB')).toBe(false);
        expect(isId('0123456789a')).toBe(false);
        expect(isId('../etc/passwd')).toBe(false);
        expect(isId(null)).toBe(false);
    });

    it('reads the article element', () => {
        expect(entryOf({ reading: ID, title: '  A title ', byline: 'By Someone', published: '2026-10-05T09:03Z' }))
            .toEqual({ id: ID, title: 'A title', byline: 'By Someone', published: '2026-10-05T09:03Z' });
        expect(entryOf({ reading: ID, title: 'A title', byline: '', published: undefined }))
            .toEqual({ id: ID, title: 'A title', byline: null, published: null });
    });

    it('rejects an element without a valid id or title', () => {
        expect(entryOf({ reading: 'nope', title: 'A title' })).toBeNull();
        expect(entryOf({ reading: ID, title: '   ' })).toBeNull();
        expect(entryOf(undefined)).toBeNull();
    });

    it('cuts very long text', () => {
        expect(entryOf({ reading: ID, title: 'x'.repeat(2000) }).title).toHaveLength(500);
    });

    it('links relative to the base', () => {
        expect(hrefOf(ID)).toBe(`read/${ID}`);
    });
});

describe('remember', () => {
    const entry = { id: ID, title: 'A title', byline: null, published: null };

    it('starts a new record', () => {
        expect(remember(undefined, entry, 1000)).toEqual({ ...entry, first: 1000, last: 1000, reads: 1 });
    });

    it('keeps the first read and counts again', () => {
        expect(remember({ ...entry, first: 10, last: 20, reads: 2 }, { ...entry, title: 'New title' }, 30))
            .toEqual({ ...entry, title: 'New title', first: 10, last: 30, reads: 3 });
    });

    it('repairs damaged records', () => {
        expect(remember({ first: 'x', reads: -4 }, entry, 5)).toEqual({ ...entry, first: 5, last: 5, reads: 1 });
    });
});

describe('ordering and pruning', () => {
    const records = [
        { id: 'aaaaaaaaaaaa', title: 'A', last: 1 },
        { id: 'bbbbbbbbbbbb', title: 'B', last: 3 },
        { id: 'cccccccccccc', title: 'C', last: 2 },
        { id: 'dddddddddddd', title: 'D', last: 3 },
        { id: 'bad', title: 'E', last: 9 },
        { id: 'eeeeeeeeeeee', title: '', last: 9 },
        null,
    ];

    it('sorts by last read, then id, and drops damaged records', () => {
        expect(shelved(records).map(record => record.title)).toEqual(['B', 'D', 'C', 'A']);
        expect(recentFirst({ id: 'a', last: 1 }, { id: 'a', last: 1 })).toBe(0);
    });

    it('lists the ids past the cap', () => {
        expect(overflow(records, 2)).toEqual(['cccccccccccc', 'aaaaaaaaaaaa']);
        expect(overflow(records)).toEqual([]);
    });
});

describe('labels', () => {
    it('shows the time today and the date otherwise', () => {
        const at = Date.UTC(2026, 10, 2, 14, 5, 9);
        expect(readLabel(at, Date.UTC(2026, 10, 2, 23))).toBe('today 14:05 UTC');
        expect(readLabel(at, Date.UTC(2026, 10, 3, 1))).toBe('2026-11-02');
        expect(readLabel(undefined)).toBe('');
    });

    it('counts repeat reads only', () => {
        expect(readsText(1)).toBe('');
        expect(readsText(undefined)).toBe('');
        expect(readsText(4)).toBe('4 reads');
    });
});

describe('visit', () => {
    it('does nothing without IndexedDB', async () => {
        await expect(visit(undefined, { id: ID, title: 'A' })).resolves.toEqual([]);
    });
});
