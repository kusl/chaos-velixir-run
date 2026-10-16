import { describe, expect, it } from 'vitest';
import {
    LOCAL_CAP,
    OFFER_LIMIT,
    PALM_SIZE,
    PEN_GRACE_MS,
    RESOLUTION,
    Result,
    backingSize,
    brushWidth,
    chunkStrokes,
    drawPath,
    farEnough,
    isPalm,
    isUuid7,
    later,
    parseDrawReply,
    pickOffers,
    quantize,
    shouldOfferReset,
    timeKey,
    toPoint,
    unseen,
} from '../../src/Chaos.Web/Components/Islands/CanvasIsland.razor.js';

describe('coordinates', () => {
    it('quantizes and clamps to the shared grid', () => {
        expect(quantize(0)).toBe(0);
        expect(quantize(1)).toBe(RESOLUTION);
        expect(quantize(-0.5)).toBe(0);
        expect(quantize(1.5)).toBe(RESOLUTION);
        expect(quantize(0.5)).toBe(2048);
    });

    it('maps client coordinates into the canvas box', () => {
        expect(toPoint(150, 250, { left: 100, top: 200, width: 100, height: 100 })).toEqual([2048, 2048]);
    });

    it('drops points that are too close together', () => {
        expect(farEnough([], 0, 0)).toBe(true);
        expect(farEnough([10, 10], 11, 11)).toBe(false);
        expect(farEnough([10, 10], 13, 10)).toBe(true);
    });
});

describe('palm rejection', () => {
    it('never rejects a pen or a mouse', () => {
        expect(isPalm({ pointerType: 'pen', width: 99, height: 99 }, 0, 0)).toBe(false);
        expect(isPalm({ pointerType: 'mouse', width: 99, height: 99 }, 0, 0)).toBe(false);
    });

    it('rejects broad touches', () => {
        expect(isPalm({ pointerType: 'touch', width: PALM_SIZE + 1, height: 5 }, -Infinity, 0)).toBe(true);
        expect(isPalm({ pointerType: 'touch', width: 10, height: 10 }, -Infinity, 0)).toBe(false);
    });

    it('rejects touches right after a pen was used', () => {
        expect(isPalm({ pointerType: 'touch', width: 1, height: 1 }, 1000, 1000 + PEN_GRACE_MS - 1)).toBe(true);
        expect(isPalm({ pointerType: 'touch', width: 1, height: 1 }, 1000, 1000 + PEN_GRACE_MS)).toBe(false);
    });
});

describe('sizing', () => {
    it('uses device pixels for the backing store', () => {
        expect(backingSize(300, 2)).toBe(600);
        expect(backingSize(0.2, 1)).toBe(1);
    });

    it('scales the brush with the canvas', () => {
        expect(brushWidth(1000)).toBe(6);
        expect(brushWidth(10)).toBe(1);
    });
});

describe('remote strokes', () => {
    it('keeps only points not seen before', () => {
        expect(unseen(0, 0, [1, 2, 3, 4])).toEqual([1, 2, 3, 4]);
        expect(unseen(1, 0, [1, 2, 3, 4])).toEqual([3, 4]);
        expect(unseen(2, 0, [1, 2, 3, 4])).toEqual([]);
        expect(unseen(2, 2, [5, 6])).toEqual([5, 6]);
    });

    it('reports a gap', () => {
        expect(unseen(1, 3, [5, 6])).toBeNull();
    });

    it('matches the server result codes', () => {
        expect(Result).toEqual({ Accepted: 0, Stale: 1, TooFast: 2, Invalid: 3, Full: 4, TooLong: 5 });
    });
});

describe('drawPath', () => {
    const recorder = () => {
        const calls = [];
        return {
            calls,
            lineWidth: 4,
            beginPath: () => calls.push(['begin']),
            arc: (x, y, r) => calls.push(['arc', x, y, r]),
            fill: () => calls.push(['fill']),
            moveTo: (x, y) => calls.push(['move', x, y]),
            lineTo: (x, y) => calls.push(['line', x, y]),
            stroke: () => calls.push(['stroke']),
        };
    };

    it('draws a dot for a single point', () => {
        const context = recorder();
        drawPath(context, RESOLUTION, [10, 20], 0);
        expect(context.calls).toEqual([['begin'], ['arc', 10, 20, 2], ['fill']]);
    });

    it('connects new points to the previous one', () => {
        const context = recorder();
        drawPath(context, RESOLUTION, [0, 0, 10, 10, 20, 20], 4);
        expect(context.calls).toEqual([['begin'], ['move', 10, 10], ['line', 20, 20], ['stroke']]);
    });

    it('does nothing without new points', () => {
        const context = recorder();
        drawPath(context, RESOLUTION, [1, 1, 2, 2], 4);
        expect(context.calls).toEqual([]);
    });
});

describe('persistence', () => {
    const id = (ms, tail = 1) => {
        const hex = ms.toString(16).padStart(12, '0');
        return `${hex.slice(0, 8)}-${hex.slice(8)}-7000-8000-${String(tail).padStart(12, '0')}`;
    };

    it('reads draw replies', () => {
        expect(parseDrawReply('0 0199e1aa-bbbb-7ccc-8ddd-eeeeeeeeeeee')).toEqual({ result: Result.Accepted, stroke: '0199e1aa-bbbb-7ccc-8ddd-eeeeeeeeeeee' });
        expect(parseDrawReply('2')).toEqual({ result: Result.TooFast, stroke: null });
        expect(parseDrawReply('0 not-an-id')).toEqual({ result: Result.Accepted, stroke: null });
        expect(parseDrawReply(7)).toEqual({ result: Result.Invalid, stroke: null });
    });

    it('builds a key bound that sorts before every id from that millisecond', () => {
        const at = Date.UTC(2026, 9, 16);
        expect(id(at - 1) < timeKey(at)).toBe(true);
        expect(id(at) > timeKey(at)).toBe(true);
        expect(isUuid7(id(at))).toBe(true);
    });

    it('picks the later reset', () => {
        expect(later(null, null)).toBeNull();
        expect(later(id(5), null)).toBe(id(5));
        expect(later(id(5), id(9))).toBe(id(9));
    });

    it('offers a remembered reset only when it is newer and from before the restart', () => {
        expect(shouldOfferReset(id(5), null, 10)).toBe(true);
        expect(shouldOfferReset(id(5), id(4), 10)).toBe(true);
        expect(shouldOfferReset(id(5), id(6), 10)).toBe(false);
        expect(shouldOfferReset(id(12), null, 10)).toBe(false);
        expect(shouldOfferReset(null, null, 10)).toBe(false);
    });

    it('picks strokes after the reset, before the restart, within the room left', () => {
        const strokes = [
            { id: id(30), points: [1, 1] },
            { id: id(9), points: [1, 1, 2, 2, 3, 3] },
            { id: id(8), points: [1, 1] },
            { id: id(7), points: [1, 1] },
            { id: id(3), points: [1, 1] },
        ];
        const picked = pickOffers(strokes, new Set([id(7)]), id(5), 20, 2);
        expect(picked.map(stroke => stroke.id)).toEqual([id(8)]);
        expect(pickOffers(strokes, new Set(), null, 20, 100, 2).map(stroke => stroke.id)).toEqual([id(9), id(8)]);
    });

    it('chunks offers under the message limit', () => {
        const stroke = { id: id(1), points: Array(1000).fill(4095) };
        const chunks = chunkStrokes([stroke, stroke, stroke, stroke, stroke, stroke], 12000);
        expect(chunks.length).toBe(3);
        expect(chunks.every(chunk => chunk.length === 2)).toBe(true);
        expect(chunkStrokes([{ id: id(1), points: Array(5000).fill(1) }], 1000)).toEqual([]);
    });

    it('keeps local history bounded', () => {
        expect(LOCAL_CAP).toBe(10000);
        expect(OFFER_LIMIT).toBe(500);
    });
});
