import { describe, expect, it } from 'vitest';
import { upgrade } from '../../src/Chaos.Web/Components/Islands/BoardIsland.razor.js';
import {
    ALL_DIFFERENT,
    AUTO_UNLOCK,
    CELEBRATE_MS,
    DB_VERSION,
    FULL_HOUSE,
    JACKPOTS,
    JACKPOT_WAYS,
    JACKPOT_OFFERS,
    LABELS,
    OUTCOMES,
    PRESS_KEY,
    SEQUENCES,
    SYMBOLS,
    STORE,
    WINNER_OFFERS,
    WINNING,
    above,
    autoDelay,
    below,
    choose,
    classify,
    duration,
    headsInARow,
    hits,
    isFullHouse,
    isJackpot,
    isValid,
    lesson,
    medianWait,
    migrations,
    newestJackpots,
    newestWinners,
    oneIn,
    parseReply,
    percent,
    progressText,
    putAll,
    readAll,
    readPresses,
    spinFrames,
    stopTimes,
    stripFor,
    verdictText,
    waysOf,
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
        ['ABCDE', 'sequenceUp'],
        ['789AB', 'sequenceUp'],
        ['VWXYZ', 'sequenceUp'],
        ['43210', 'sequenceDown'],
        ['BA987', 'sequenceDown'],
        ['YZ012', 'none'],
        ['ABCDF', 'up'],
        ['ZK8A0', 'none'],
        ['AABBC', 'none'],
        ['ABCDD', 'none'],
    ])('classifies %s as %s', (text, expected) => {
        expect(classify(text)).toBe(expected);
    });

    it('labels every kind of win', () => {
        expect(Object.keys(LABELS).sort()).toEqual(['down', 'five', 'four', 'sequenceDown', 'sequenceUp', 'three', 'up']);
    });

    it('marks the symbols that won', () => {
        expect(hits('7Q7A7')).toEqual([true, false, true, false, true]);
        expect(hits('ABCDE')).toEqual([true, true, true, true, true]);
        expect(hits('43210')).toEqual([true, true, true, true, true]);
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

describe('odds', () => {
    it('counts every kind the same way an exhaustive search does', () => {
        const symbols = '89ABCD';
        const counts = { none: 0, three: 0, four: 0, five: 0, up: 0, down: 0, sequenceUp: 0, sequenceDown: 0 };
        const total = symbols.length ** 5;
        for (let index = 0; index < total; index += 1) {
            let text = '';
            for (let rest = index, reel = 0; reel < 5; reel += 1, rest = Math.floor(rest / symbols.length)) {
                text += symbols[rest % symbols.length];
            }
            counts[classify(text)] += 1;
        }
        for (const kind of Object.keys(LABELS)) {
            expect(counts[kind]).toBe(waysOf(kind, symbols.length));
        }
    });

    it('knows the real totals', () => {
        expect(OUTCOMES).toBe(60_466_176);
        expect(waysOf('five')).toBe(36);
        expect(waysOf('four')).toBe(6_300);
        expect(waysOf('three')).toBe(441_000);
        expect(waysOf('up')).toBe(376_960);
        expect(waysOf('down')).toBe(376_960);
        expect(waysOf('sequenceUp')).toBe(32);
        expect(waysOf('sequenceDown')).toBe(32);
        expect(SEQUENCES).toBe(64);
        expect(JACKPOT_WAYS).toBe(6_400);
        expect(waysOf('none')).toBe(0);
        expect(WINNING).toBe(1_201_320);
        expect(FULL_HOUSE).toBe(12_600);
        expect(ALL_DIFFERENT).toBe(45_239_040);
        expect(choose(36, 5)).toBe(376_992);
        expect(choose(5, 6)).toBe(0);
    });

    it('formats chances for people', () => {
        expect(oneIn(WINNING)).toBe('50.3');
        expect(oneIn(waysOf('four'))).toBe('9,598');
        expect(oneIn(SEQUENCES)).toBe('944,784');
        expect(oneIn(JACKPOT_WAYS)).toBe('9,448');
        expect(percent(waysOf('three') / OUTCOMES)).toBe('0.729%');
        expect(percent(waysOf('five') / OUTCOMES)).toBe('0.0000595%');
        expect(duration(4000)).toBe('4 seconds');
        expect(duration(1000)).toBe('1 second');
        expect(duration(548_000)).toBe('9.1 minutes');
        expect(duration(36 ** 4 * 4000)).toBe('78 days');
    });

    it('separates the median wait from the average', () => {
        expect(medianWait(0.5)).toBe(1);
        expect(medianWait(waysOf('three') / OUTCOMES)).toBe(95);
        expect(headsInARow(1 / 8192)).toBe(13);
        expect(headsInARow(0.9)).toBe(1);
    });

    it('spots three with a pair', () => {
        expect(isFullHouse('77QQ7')).toBe(true);
        expect(isFullHouse('7Q7A7')).toBe(false);
        expect(isFullHouse('ABCDE')).toBe(false);
    });

    it('explains only winners', () => {
        expect(lesson('ZK8A0')).toBeNull();
        const four = lesson('XXXXB', 4000);
        expect(four.kind).toBe('four');
        expect(four.headline).toBe('1 in 9,598');
        expect(four.facts.map(([term]) => term)).toContain('Half the time within');
        expect(four.sections.some(([, text]) => text.includes('XXXXB'))).toBe(true);
        expect(lesson('77QQ7').facts.at(-1)[1]).toBe('12,600 ways, 1 in 4,799');
        expect(lesson('ZKA80').sections[0][1]).toContain('falls');
        const run = lesson('ABCDE');
        expect(run.kind).toBe('sequenceUp');
        expect(run.facts).toContainEqual(['Either direction', '64 ways, 1 in 944,784']);
        expect(run.facts).toContainEqual(['Any jackpot', '6,400 ways, 1 in 9,448']);
        expect(lesson('43210').sections[0][1]).toContain('43210');
        expect(lesson('13AKZ').facts.map(([term]) => term)).not.toContain('Any jackpot');
    });

    it('offers the newest winners from local history', () => {
        const records = [
            { id: '0199a000-0000-7000-8000-000000000001', text: 'AAAB1' },
            { id: '0199a000-0000-7000-8000-000000000002', text: 'ZK8A0' },
            { id: '0199a000-0000-7000-8000-000000000003', text: '13AKZ' },
            { id: '0199a000-0000-7000-8000-000000000004', text: 'bad' },
        ];
        expect(newestWinners(records).map(record => record.text)).toEqual(['13AKZ', 'AAAB1']);
        expect(newestWinners(records, 1)).toHaveLength(1);
    });
});

describe('jackpots', () => {
    it.each([
        ['XXXXB', true],
        ['KKKKK', true],
        ['7Q7A7', false],
        ['ABCDE', true],
        ['43210', true],
        ['VWXYZ', true],
        ['13AKZ', false],
        ['YZ012', false],
        ['ZK8A0', false],
        ['kkkkk', false],
        [null, false],
    ])('knows whether %s is a jackpot', (text, expected) => {
        expect(isJackpot(text)).toBe(expected);
    });

    it('shouts about jackpots in the verdict', () => {
        expect(verdictText('KKKKK')).toBe('Jackpot! Five of a kind');
        expect(verdictText('XXXXB')).toBe('Jackpot! Four of a kind');
        expect(verdictText('ABCDE')).toBe('Jackpot! Sequence up');
        expect(verdictText('43210')).toBe('Jackpot! Sequence down');
        expect(verdictText('13AKZ')).toBe('Straight up');
        expect(verdictText('7Q7A7')).toBe('Three of a kind');
        expect(verdictText('ZK8A0')).toBe('');
    });

    it('keeps jackpots in their own store added by the second schema version', () => {
        expect(DB_VERSION).toBe(2);
        expect(migrations).toHaveLength(2);
        const created = [];
        upgrade({ createObjectStore: (name, options) => created.push([name, options]) }, 1, DB_VERSION, migrations);
        expect(created).toEqual([[JACKPOTS, { keyPath: 'id' }]]);
        expect(JACKPOTS).not.toBe(STORE);
    });

    it('offers every jackpot newest first', () => {
        const records = [
            { id: '0199a000-0000-7000-8000-000000000001', text: 'XXXXB' },
            { id: '0199a000-0000-7000-8000-000000000002', text: 'AAAB1' },
            { id: '0199a000-0000-7000-8000-000000000003', text: 'KKKKK' },
            { id: '0199a000-0000-7000-8000-000000000004', text: 'kkkkk' },
            { id: '0199a000-0000-7000-8000-000000000005', text: '13AKZ' },
            { id: '0199a000-0000-7000-8000-000000000006', text: '43210' },
        ];
        expect(newestJackpots(records).map(record => record.text)).toEqual(['43210', 'KKKKK', 'XXXXB']);
        expect(newestJackpots(records, 1)).toHaveLength(1);
        expect(JACKPOT_OFFERS).toBe(500);
        expect(WINNER_OFFERS).toBe(500);
    });

    it('celebrates for a few seconds', () => {
        expect(CELEBRATE_MS).toBeGreaterThanOrEqual(3000);
        expect(CELEBRATE_MS).toBeLessThanOrEqual(15000);
    });

    it('reads and writes whole stores', async () => {
        const rows = new Map();
        const db = {
            transaction(store, mode) {
                const transaction = {
                    objectStore(name) {
                        expect(name).toBe(store);
                        return {
                            put(record) {
                                expect(mode).toBe('readwrite');
                                rows.set(record.id, record);
                            },
                            getAll() {
                                const request = {};
                                queueMicrotask(() => {
                                    request.result = [...rows.values()];
                                    request.onsuccess();
                                });
                                return request;
                            },
                        };
                    },
                };
                queueMicrotask(() => transaction.oncomplete?.());
                return transaction;
            },
        };
        await putAll(db, JACKPOTS, []);
        expect(rows.size).toBe(0);
        await putAll(db, JACKPOTS, [{ id: 'a', text: 'KKKKK' }, { id: 'b', text: 'XXXXB' }]);
        expect((await readAll(db, JACKPOTS)).map(record => record.text)).toEqual(['KKKKK', 'XXXXB']);
    });
});
