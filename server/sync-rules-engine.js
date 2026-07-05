const path = require('path');

function normalizePath(filePath) {
    if (!filePath) return '/';
    return filePath.startsWith('/') ? filePath : `/${filePath}`;
}

function isFlagFile(filePath) {
    return filePath.endsWith('.flag') || filePath.endsWith('.ready');
}

function getDataPathForFlag(filePath) {
    if (filePath.endsWith('.flag')) {
        return filePath.slice(0, -'.flag'.length);
    }
    if (filePath.endsWith('.ready')) {
        return filePath.slice(0, -'.ready'.length);
    }
    return filePath;
}

function findOrderGroup(filePath, syncRules) {
    const fileName = path.basename(normalizePath(filePath));
    for (const group of syncRules?.orderGroups || []) {
        const patterns = (group.pattern || '').split(',').map(p => p.trim()).filter(Boolean);
        for (const pattern of patterns) {
            const regex = new RegExp('^' + pattern.replace(/\./g, '\\.').replace(/\*/g, '.*') + '$', 'i');
            if (regex.test(fileName)) {
                return group;
            }
        }
    }
    return null;
}

/**
 * @param {string} filePath
 * @param {object} syncRules
 * @param {(path: string) => Promise<boolean>} fileExists - returns true if dependency is ready
 */
async function resolveUploadStatus(filePath, syncRules, fileExists, syncGroupId = null) {
    const normalized = normalizePath(filePath);
    const group = findOrderGroup(normalized, syncRules);

    if (group?.action === 'data_before_flag' && isFlagFile(normalized)) {
        const dataPath = getDataPathForFlag(normalized);
        const dataReady = await fileExists(dataPath);
        if (!dataReady) {
            return { syncStatus: 'waiting', syncGroupId: syncGroupId || group.id };
        }
    }

    if (group?.action === 'sequential' && Array.isArray(group.order)) {
        const fileName = path.basename(normalized);
        const fileIndex = group.order.indexOf(fileName);
        if (fileIndex > 0) {
            for (let i = 0; i < fileIndex; i += 1) {
                const prevName = group.order[i];
                const prevPath = path.posix.join(path.posix.dirname(normalized), prevName);
                const prevReady = await fileExists(prevPath);
                if (!prevReady) {
                    return { syncStatus: 'waiting', syncGroupId: syncGroupId || group.id };
                }
            }
        }
    }

    return { syncStatus: 'ready', syncGroupId: syncGroupId || group?.id || null };
}

module.exports = {
    normalizePath,
    isFlagFile,
    getDataPathForFlag,
    findOrderGroup,
    resolveUploadStatus
};
