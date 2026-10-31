import { describe, expect, it } from 'vitest';
import {
    LOCAL_CAP,
    META_FIELDS,
    OFFER_LIMIT,
    SHARE_KEY,
    chunkOffers,
    collectMeta,
    countText,
    excessOf,
    formatOffset,
    isoOf,
    labelOf,
    localStamp,
    metaLabel,
    needsResync,
    newestFirst,
    readShare,
    shareDefault,
    timestampOf,
    toRecord,
    upgrade,
    writeShare,
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

    it('counts details toward the size', () => {
        const meta = [{ key: 'userAgent', value: 'x'.repeat(300) }];
        const messages = Array.from({ length: 4 }, (_, index) => ({ ...message(index, 'x'.repeat(100)), meta }));
        expect(chunkOffers(messages, 1000).every(chunk => chunk.length === 2)).toBe(true);
        expect(chunkOffers(messages.map(({ meta: _, ...rest }) => rest), 1000)).toHaveLength(1);
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

describe('countText', () => {
    it('shows the length against the limit', () => {
        expect(countText(0, 500)).toBe('0 / 500');
        expect(countText(137, 500)).toBe('137 / 500');
        expect(countText(3, -1)).toBe('3');
    });
});

describe('message details', () => {
    const keys = [
        'latitude', 'longitude', 'accuracy', 'altitude', 'altitudeAccuracy', 'heading', 'speed', 'positionAt',
        'timeZone', 'utcOffset', 'localTime', 'locale', 'calendar', 'numberingSystem', 'language', 'languages',
        'userAgent', 'brands', 'platform', 'mobile', 'vendor', 'webdriver', 'doNotTrack', 'globalPrivacyControl',
        'pdfViewer', 'screen', 'availableScreen', 'viewport', 'pixelRatio', 'colorDepth', 'orientation', 'cores',
        'memory', 'touchPoints', 'pointer', 'colorScheme', 'reducedMotion', 'contrast', 'online', 'connection',
        'downlink', 'rtt', 'saveData',
    ];

    const position = (coords = {}) => ({
        timestamp: Date.UTC(2026, 9, 30, 12),
        coords: { latitude: 27.7172, longitude: 85.324, accuracy: 35, altitude: null, altitudeAccuracy: null, heading: Number.NaN, speed: null, ...coords },
    });

    const browser = {
        navigator: {
            language: 'en-US',
            languages: ['en-US', 'ne'],
            userAgent: 'TestAgent/1.0',
            userAgentData: { brands: [{ brand: 'Chromium', version: '140' }], platform: 'Linux', mobile: false },
            platform: 'Linux x86_64',
            vendor: 'Google Inc.',
            webdriver: true,
            doNotTrack: '1',
            pdfViewerEnabled: true,
            hardwareConcurrency: 8,
            deviceMemory: 4,
            maxTouchPoints: 0,
            onLine: true,
            connection: { effectiveType: '4g', downlink: 10, rtt: 50, saveData: false },
        },
        screen: { width: 1920, height: 1080, availWidth: 1920, availHeight: 1040, colorDepth: 24, orientation: { type: 'landscape-primary' } },
        innerWidth: 1280,
        innerHeight: 720,
        devicePixelRatio: 1.5,
        matchMedia: query => ({ matches: ['(pointer: fine)', '(prefers-color-scheme: dark)'].includes(query) }),
        Intl: { DateTimeFormat: () => ({ resolvedOptions: () => ({ timeZone: 'Asia/Kathmandu', locale: 'en-US', calendar: 'gregory', numberingSystem: 'latn' }) }) },
    };

    const now = { getTimezoneOffset: () => -345, getFullYear: () => 2026, getMonth: () => 9, getDate: () => 30, getHours: () => 17, getMinutes: () => 45, getSeconds: () => 3 };

    it('uses the same keys in the same order as the server', () => {
        expect(META_FIELDS.map(([key]) => key)).toEqual(keys);
        expect(new Set(META_FIELDS.map(([, label]) => label)).size).toBe(keys.length);
    });

    it('labels known keys and falls back to the key', () => {
        expect(metaLabel('latitude')).toBe('Latitude');
        expect(metaLabel('cores')).toBe('CPU threads');
        expect(metaLabel('mystery')).toBe('mystery');
    });

    it('needs a position', () => {
        expect(collectMeta(null, browser)).toBeNull();
        expect(collectMeta({ coords: { latitude: Number.NaN, longitude: 1 } }, browser)).toBeNull();
    });

    it('collects what the browser reports, in catalog order, without empty values', () => {
        const entries = collectMeta(position(), browser, now);
        const values = Object.fromEntries(entries.map(entry => [entry.key, entry.value]));
        expect(entries.map(entry => entry.key)).toEqual(keys.filter(key => key in values));
        expect(values).toMatchObject({
            latitude: '27.7172',
            longitude: '85.324',
            accuracy: '35',
            positionAt: '2026-10-30T12:00:00.000Z',
            timeZone: 'Asia/Kathmandu',
            utcOffset: '+05:45',
            localTime: '2026-10-30 17:45:03',
            languages: 'en-US, ne',
            brands: 'Chromium 140',
            platform: 'Linux',
            mobile: 'no',
            webdriver: 'yes',
            screen: '1920x1080',
            availableScreen: '1920x1040',
            viewport: '1280x720',
            pixelRatio: '1.5',
            orientation: 'landscape-primary',
            cores: '8',
            memory: '4',
            touchPoints: '0',
            pointer: 'fine',
            colorScheme: 'dark',
            reducedMotion: 'no',
            contrast: 'no preference',
            connection: '4g',
            downlink: '10',
            rtt: '50',
            saveData: 'no',
        });
        expect(values).not.toHaveProperty('altitude');
        expect(values).not.toHaveProperty('heading');
        expect(values).not.toHaveProperty('speed');
    });

    it('works in a bare browser', () => {
        const entries = collectMeta(position({ altitude: 1400, heading: 90, speed: 0 }), {}, now);
        const values = Object.fromEntries(entries.map(entry => [entry.key, entry.value]));
        expect(values).toMatchObject({ altitude: '1400', heading: '90', speed: '0', utcOffset: '+05:45' });
        expect(values).not.toHaveProperty('pointer');
        expect(values).not.toHaveProperty('userAgent');
    });

    it('formats UTC offsets', () => {
        expect(formatOffset(0)).toBe('+00:00');
        expect(formatOffset(345)).toBe('+05:45');
        expect(formatOffset(-240)).toBe('-04:00');
        expect(formatOffset(-570)).toBe('-09:30');
        expect(formatOffset(Number.NaN)).toBeNull();
    });

    it('stamps local time', () => {
        expect(localStamp(new Date(2026, 0, 2, 3, 4, 5))).toBe('2026-01-02 03:04:05');
    });

    it('stores details and ownership only when present', () => {
        const meta = [{ key: 'latitude', value: '1' }, { key: 'longitude', value: '2' }];
        expect(toRecord({ id: 'a', text: 't', meta }, true)).toEqual({ id: 'a', text: 't', meta, mine: true });
        expect(toRecord({ id: 'a', text: 't', meta: [] }, false)).toEqual({ id: 'a', text: 't' });
        expect(toRecord({ id: 'a', text: 't' }, false)).toEqual({ id: 'a', text: 't' });
    });
});

describe('sharing choice', () => {
    const storage = () => {
        const values = new Map();
        return { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value) };
    };

    it('follows the permission unless turned off', () => {
        expect(shareDefault(null, 'granted')).toBe(true);
        expect(shareDefault('on', 'granted')).toBe(true);
        expect(shareDefault('off', 'granted')).toBe(false);
        expect(shareDefault('on', 'denied')).toBe(false);
        expect(shareDefault('on', 'prompt')).toBe(false);
        expect(shareDefault(null, 'prompt')).toBe(false);
        expect(shareDefault('on', null)).toBe(true);
        expect(shareDefault(null, null)).toBe(false);
    });

    it('remembers only on and off', () => {
        const store = storage();
        expect(readShare(store)).toBeNull();
        writeShare(store, 'on');
        expect(store.getItem(SHARE_KEY)).toBe('on');
        expect(readShare(store)).toBe('on');
        store.setItem(SHARE_KEY, 'maybe');
        expect(readShare(store)).toBeNull();
    });

    it('survives storage that throws', () => {
        const broken = { getItem: () => { throw new Error('denied'); }, setItem: () => { throw new Error('denied'); } };
        expect(readShare(broken)).toBeNull();
        expect(() => writeShare(broken, 'on')).not.toThrow();
        expect(readShare(undefined)).toBeNull();
    });
});
