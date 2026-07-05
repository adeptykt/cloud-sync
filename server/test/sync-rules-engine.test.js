const test = require('node:test');
const assert = require('node:assert/strict');
const {
    normalizePath,
    isFlagFile,
    getDataPathForFlag,
    findOrderGroup,
    resolveUploadStatus
} = require('../sync-rules-engine');

const sampleRules = {
    orderGroups: [
        {
            id: 'database-backup',
            pattern: '*.dat,*.flag',
            action: 'data_before_flag'
        },
        {
            id: 'multi-file-set',
            pattern: '*.json,*.bin,*.ready',
            action: 'sequential',
            order: ['config.json', 'data.bin', 'data.ready']
        }
    ]
};

test('normalizePath adds leading slash', () => {
    assert.equal(normalizePath('foo/bar'), '/foo/bar');
    assert.equal(normalizePath('/foo/bar'), '/foo/bar');
});

test('isFlagFile detects flag extensions', () => {
    assert.equal(isFlagFile('/exchange/prices.flag'), true);
    assert.equal(isFlagFile('/exchange/prices.ready'), true);
    assert.equal(isFlagFile('/exchange/prices.csv'), false);
});

test('getDataPathForFlag strips suffix', () => {
    assert.equal(getDataPathForFlag('/backup/1c_dump.flag'), '/backup/1c_dump');
    assert.equal(getDataPathForFlag('/exchange/prices.ready'), '/exchange/prices');
});

test('findOrderGroup matches pattern', () => {
    const group = findOrderGroup('/folder/1c_dump.flag', sampleRules);
    assert.equal(group.id, 'database-backup');
});

test('resolveUploadStatus waits for data before flag', async () => {
    const ready = new Set(['/backup/1c_dump.dat']);
    const result = await resolveUploadStatus(
        '/backup/1c_dump.flag',
        sampleRules,
        async (p) => ready.has(normalizePath(p))
    );
    assert.equal(result.syncStatus, 'waiting');

    ready.add('/backup/1c_dump');
    const readyResult = await resolveUploadStatus(
        '/backup/1c_dump.flag',
        sampleRules,
        async (p) => ready.has(normalizePath(p))
    );
    assert.equal(readyResult.syncStatus, 'ready');
});

test('resolveUploadStatus enforces sequential order', async () => {
    const ready = new Set(['/set/config.json']);

    const canUploadBin = await resolveUploadStatus(
        '/set/data.bin',
        sampleRules,
        async (p) => ready.has(normalizePath(p))
    );
    assert.equal(canUploadBin.syncStatus, 'ready');

    const waitingReady = await resolveUploadStatus(
        '/set/data.ready',
        sampleRules,
        async (p) => ready.has(normalizePath(p))
    );
    assert.equal(waitingReady.syncStatus, 'waiting');

    ready.add('/set/data.bin');
    const ok = await resolveUploadStatus(
        '/set/data.ready',
        sampleRules,
        async (p) => ready.has(normalizePath(p))
    );
    assert.equal(ok.syncStatus, 'ready');
});

test('resolveUploadStatus allows first sequential file immediately', async () => {
    const result = await resolveUploadStatus(
        '/set/config.json',
        sampleRules,
        async () => false
    );
    assert.equal(result.syncStatus, 'ready');
});
