import { describe, expect, it } from 'vitest';
import {
    PALM_SIZE,
    PEN_GRACE_MS,
    RESOLUTION,
    Result,
    backingSize,
    brushWidth,
    drawPath,
    farEnough,
    isPalm,
    quantize,
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
