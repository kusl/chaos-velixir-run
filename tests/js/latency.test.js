import { describe, expect, it } from 'vitest';
import {
    AUTO_UNLOCK,
    cacheBusted,
    clockText,
    fetchOptions,
    measure,
    measureAll,
    median,
    ms,
    parseRun,
    parseStartReply,
    progressText,
    readNumber,
    summarize,
} from '../../src/Chaos.Web/Components/Islands/LatencyIsland.razor.js';

const RUNS = [
    ['google=12,3;aws-us-east-1=40,', [{ key: 'google', browser: 12, server: 3 }, { key: 'aws-us-east-1', browser: 40, server: null }]],
    ['chaos=,', [{ key: 'chaos', browser: null, server: null }]],
    ['a=0,60000', [{ key: 'a', browser: 0, server: 60000 }]],
];

const INVALID = ['', 'google', 'google=1', 'google=01,2', 'Google=1,2', 'a=1,2;a=3,4', 'a=60001,1', 'a=-1,1', 'a=1,2,3', '=1,2', 'a=1.5,2'];

describe('run text', () => {
    it.each(RUNS)('parses %s', (text, expected) => {
        expect(parseRun(text)).toEqual(expected);
    });

    it.each(INVALID)('rejects %s', text => {
        expect(parseRun(text)).toBeNull();
    });

    it('summarizes with medians and answer counts', () => {
        const results = parseRun('a=10,1;b=30,;c=,5;d=,');
        expect(summarize(results)).toEqual({ browser: 20, server: 3, answered: 3, total: 4 });
    });

    it('rounds even medians half up', () => {
        expect(median([1, 2])).toBe(2);
        expect(median([3, null, 1, 2])).toBe(2);
        expect(median([null])).toBeNull();
    });

    it('formats milliseconds', () => {
        expect(ms(12)).toBe('12 ms');
        expect(ms(null)).toBe('–');
    });
});

describe('start replies', () => {
    it('reads every reply', () => {
        expect(parseStartReply('ok 0199e1aa-bbbb-7ccc-8ddd-eeeeeeeeeeee 300')).toEqual({ kind: 'ok', id: '0199e1aa-bbbb-7ccc-8ddd-eeeeeeeeeeee', seconds: 300 });
        expect(parseStartReply('busy')).toEqual({ kind: 'busy' });
        expect(parseStartReply('wait 42')).toEqual({ kind: 'wait', seconds: 42 });
        expect(parseStartReply('wait x')).toEqual({ kind: 'error' });
        expect(parseStartReply('ok nope 300')).toEqual({ kind: 'error' });
        expect(parseStartReply(null)).toEqual({ kind: 'error' });
    });
});

describe('text helpers', () => {
    it('counts down in minutes and seconds', () => {
        expect(clockText(0)).toBe('0:00');
        expect(clockText(299_001)).toBe('5:00');
        expect(clockText(61_000)).toBe('1:01');
    });

    it('counts towards auto run', () => {
        expect(progressText(0)).toBe(`${AUTO_UNLOCK} more tests unlock auto run.`);
        expect(progressText(AUTO_UNLOCK - 1)).toBe('1 more test unlocks auto run.');
        expect(progressText(12)).toBe('12 tests from this browser.');
    });

    it('reads stored numbers defensively', () => {
        expect(readNumber({ getItem: () => '7' }, 'k')).toBe(7);
        expect(readNumber({ getItem: () => 'x' }, 'k')).toBe(0);
        expect(readNumber({ getItem() { throw new Error('denied'); } }, 'k')).toBe(0);
    });
});

describe('requests', () => {
    it('sends opaque requests without credentials to other sites', () => {
        expect(fetchOptions('https://example.com/', null)).toMatchObject({ mode: 'no-cors', cache: 'no-store', credentials: 'omit', referrerPolicy: 'no-referrer' });
        expect(fetchOptions('/time', null).mode).toBeUndefined();
        expect(cacheBusted('/time', 3)).toBe('/time?n=3');
        expect(cacheBusted('https://example.com/a?b=1', 3)).toBe('https://example.com/a?b=1');
    });

    const fake = (durations, failAfter = Infinity) => {
        let clock = 0;
        let calls = 0;
        return {
            calls: () => calls,
            deps: {
                now: () => clock,
                setTimeout: () => 0,
                clearTimeout: () => {},
                AbortController,
                fetch: async () => {
                    calls += 1;
                    if (calls > failAfter) {
                        throw new Error('down');
                    }
                    clock += durations[(calls - 1) % durations.length];
                    return {};
                },
            },
        };
    };

    it('warms up and keeps the fastest of three', async () => {
        const probe = fake([100, 30, 20, 25]);
        expect(await measure('https://example.com/', probe.deps)).toBe(20);
        expect(probe.calls()).toBe(4);
    });

    it('gives up when the warm up fails', async () => {
        const probe = fake([5], 0);
        expect(await measure('https://example.com/', probe.deps)).toBeNull();
        expect(probe.calls()).toBe(1);
    });

    it('keeps what it has when a later sample fails', async () => {
        const probe = fake([50, 9], 2);
        expect(await measure('https://example.com/', probe.deps)).toBe(9);
    });

    it('measures every endpoint and reports each one', async () => {
        const probe = fake([4]);
        const seen = [];
        const results = await measureAll(['a', 'b', 'c'], probe.deps, 1000, (index, value) => seen.push([index, value]), 1);
        expect(results).toEqual([4, 4, 4]);
        expect(seen.map(entry => entry[0]).sort()).toEqual([0, 1, 2]);
    });
});
