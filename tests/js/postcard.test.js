import { describe, expect, it } from 'vitest';
import {
    DB_NAME,
    IMAGE_PATH,
    LOCAL_CAP,
    MAX_IMAGES,
    MAX_IMAGE_BYTES,
    MAX_SIDE,
    MAX_TEXT,
    MAX_TOTAL_BYTES,
    RESIZE_SIDE,
    cleanText,
    fitSize,
    hasText,
    imageKey,
    imageRange,
    imageUrl,
    isComplete,
    isStale,
    merge,
    needsResize,
    pickProblem,
    toRecord,
} from '../../src/Chaos.Web/Components/Islands/PostcardIsland.razor.js';
import { DB_NAME as BOARD_DB } from '../../src/Chaos.Web/Components/Islands/BoardIsland.razor.js';

const ID = '0199a000-0000-7000-8000-000000000001';

describe('limits', () => {
    it('matches the server rules', () => {
        expect(MAX_TEXT).toBe(5000);
        expect(MAX_IMAGES).toBe(4);
        expect(MAX_IMAGE_BYTES).toBe(2 * 1024 * 1024);
        expect(MAX_TOTAL_BYTES).toBe(4 * 1024 * 1024);
        expect(MAX_SIDE).toBe(4096);
        expect(LOCAL_CAP).toBe(10000);
        expect(DB_NAME).not.toBe(BOARD_DB);
    });
});

describe('image keys', () => {
    it('sort with their postcard and stay inside its range', () => {
        const [low, high] = imageRange(ID);
        expect(imageKey(ID, 0) > low).toBe(true);
        expect(imageKey(ID, 3) < high).toBe(true);
        expect(imageKey('0199a000-0000-7000-8000-000000000002', 0) > high).toBe(true);
    });

    it('point at the server endpoint', () => {
        expect(imageUrl(ID, 2)).toBe(`${IMAGE_PATH}/${ID}/2`);
    });
});

describe('cleanText', () => {
    it('keeps newlines and drops other control characters', () => {
        expect(cleanText('a\r\nb\rc\u0000\u0007\td\u0085')).toBe('a\nb\ncd');
    });

    it('replaces lone surrogates so the payload stays small', () => {
        expect(cleanText('x\ud800y')).toBe('x�y');
    });

    it('treats null as empty', () => {
        expect(cleanText(null)).toBe('');
    });
});

describe('hasText', () => {
    it('ignores whitespace and zero-width characters', () => {
        expect(hasText(' \n​ ')).toBe(false);
        expect(hasText(' a ')).toBe(true);
    });
});

describe('resizing', () => {
    it('keeps small images and scales the longest side down', () => {
        expect(fitSize(800, 600)).toEqual({ width: 800, height: 600 });
        expect(fitSize(4000, 3000)).toEqual({ width: RESIZE_SIDE, height: 1536 });
        expect(fitSize(3000, 4000)).toEqual({ width: 1536, height: RESIZE_SIDE });
        expect(fitSize(10000, 1)).toEqual({ width: RESIZE_SIDE, height: 1 });
    });

    it('resizes large photos but never GIFs', () => {
        expect(needsResize('image/jpeg', MAX_IMAGE_BYTES + 1, 100, 100)).toBe(true);
        expect(needsResize('image/png', 100, MAX_SIDE + 1, 100)).toBe(true);
        expect(needsResize('image/jpeg', 100, 100, 100)).toBe(false);
        expect(needsResize('image/gif', MAX_IMAGE_BYTES + 1, MAX_SIDE + 1, 1)).toBe(false);
    });
});

describe('pickProblem', () => {
    it('accepts PNG, JPEG and GIF within the limits', () => {
        expect(pickProblem('image/png', 10, 0, 0)).toBeNull();
        expect(pickProblem('image/jpeg', null, 3, MAX_TOTAL_BYTES)).toBeNull();
        expect(pickProblem('image/gif', MAX_IMAGE_BYTES, 0, 0)).toBeNull();
    });

    it('explains every refusal', () => {
        expect(pickProblem('image/png', 10, MAX_IMAGES, 0)).toMatch(/at most 4/);
        expect(pickProblem('image/webp', 10, 0, 0)).toMatch(/PNG, JPEG and GIF/);
        expect(pickProblem('image/svg+xml', 10, 0, 0)).toMatch(/PNG, JPEG and GIF/);
        expect(pickProblem('image/gif', MAX_IMAGE_BYTES + 1, 0, 0)).toMatch(/2 MB/);
        expect(pickProblem('image/gif', 10, 1, MAX_TOTAL_BYTES)).toMatch(/4 MB/);
    });
});

describe('records', () => {
    const card = { id: ID, text: 'hi', images: [{ type: 'image/png', width: 4, height: 3, length: 99, extra: 1 }] };

    it('keep only known fields and mark mine', () => {
        expect(toRecord(card, false)).toEqual({ id: ID, text: 'hi', images: [{ type: 'image/png', width: 4, height: 3, length: 99 }] });
        expect(toRecord({ ...card, meta: [{ key: 'latitude', value: '1' }] }, true)).toMatchObject({ mine: true, meta: [{ key: 'latitude', value: '1' }] });
        expect(toRecord({ id: ID }, false)).toEqual({ id: ID, text: '', images: [] });
    });

    it('never forget that a postcard was mine', () => {
        const record = toRecord(card, false);
        expect(merge({ ...record, mine: true }, record).mine).toBe(true);
        expect(merge(null, record)).not.toHaveProperty('mine');
        expect(merge(record, { ...record, mine: true }).mine).toBe(true);
    });

    it('are complete only with every image at its size', () => {
        const record = toRecord(card, false);
        expect(isComplete(record, [new Blob([new Uint8Array(99)])])).toBe(true);
        expect(isComplete(record, [new Blob([new Uint8Array(98)])])).toBe(false);
        expect(isComplete(record, [null])).toBe(false);
        expect(isComplete(toRecord({ id: ID, text: 'x' }, false), [])).toBe(true);
    });
});

describe('isStale', () => {
    it('drops changes older than what was shown but never snapshots', () => {
        expect(isStale(3, { kind: 'change', version: 3 })).toBe(true);
        expect(isStale(3, { kind: 'change', version: 4 })).toBe(false);
        expect(isStale(3, { kind: 'snapshot', version: 1 })).toBe(false);
    });
});
