const {
    normalizePath,
    findOrderGroup,
    resolveUploadStatus
} = require('../sync-rules-engine');

const { v4: uuidv4 } = require('uuid');
const fs = require('fs-extra');
const path = require('path');
const crypto = require('crypto');

async function getUserRoot(db, config, userId) {
    const user = await db.get('SELECT root_folder FROM users WHERE id = ?', userId);
    const root = user?.root_folder || path.join(config.storage.files, 'users', userId);
    if (!user?.root_folder) {
        await db.run('UPDATE users SET root_folder = ? WHERE id = ?', root, userId);
    }
    await fs.ensureDir(root);
    return root;
}

async function fileExistsForUser(db, userId, filePath) {
    const normalized = normalizePath(filePath);
    const file = await db.get(
        `SELECT id FROM user_files
         WHERE user_id = ? AND path = ? AND is_deleted = 0 AND sync_status = 'ready'`,
        userId,
        normalized
    );
    return !!file;
}

async function resolveUploadStatusForUser(db, userId, filePath, syncRules, syncGroupId) {
    return resolveUploadStatus(
        filePath,
        syncRules,
        (p) => fileExistsForUser(db, userId, p),
        syncGroupId
    );
}

async function releaseWaitingFiles(db, config, userId, syncRules, wsServer) {
    const waitingFiles = await db.all(
        `SELECT * FROM user_files
         WHERE user_id = ? AND sync_status = 'waiting' AND is_deleted = 0`,
        userId
    );

    for (const waitingFile of waitingFiles) {
        const { syncStatus } = await resolveUploadStatusForUser(
            db,
            userId,
            waitingFile.path,
            syncRules,
            waitingFile.sync_group_id
        );
        if (syncStatus !== 'ready') continue;

        await db.run(
            `UPDATE user_files SET sync_status = 'ready', updated_at = ? WHERE id = ?`,
            Date.now(),
            waitingFile.id
        );

        const change = {
            action: 'ready',
            path: waitingFile.path,
            hash: waitingFile.hash,
            size: waitingFile.size,
            timestamp: Date.now()
        };

        await db.run(
            `INSERT INTO file_changes (id, user_id, path, action, hash, size, timestamp, synced)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
            uuidv4(),
            userId,
            waitingFile.path,
            'ready',
            waitingFile.hash,
            waitingFile.size,
            change.timestamp,
            1
        );

        if (wsServer) {
            wsServer.broadcastUserFileChange(userId, change);
        }
    }
}

function registerFileRoutes(app, { db, config, getSyncRules, upload, authenticate, wsServer }) {
    const getChanges = async (req, res) => {
        const since = Number(req.query.since || 0);
        const userId = req.user.userId;

        try {
            const changes = await db.all(
                `SELECT path, action, hash, size, timestamp
                 FROM file_changes
                 WHERE user_id = ? AND timestamp > ?
                 ORDER BY timestamp ASC`,
                userId,
                since
            );

            res.json({ changes, current_time: Date.now() });
        } catch (error) {
            console.error('Ошибка получения изменений:', error);
            res.status(500).json({ error: 'Ошибка получения изменений' });
        }
    };

    const checkFile = async (req, res) => {
        const filePath = normalizePath(req.query.path || '');
        const exists = await fileExistsForUser(db, req.user.userId, filePath);
        res.type('text/plain').send(exists ? 'true' : 'false');
    };

    const uploadFile = async (req, res) => {
        try {
            const filePath = normalizePath(req.body.filePath || req.body.path || '');
            const uploadedFile = req.file;
            const userId = req.user.userId;
            const syncGroupId = req.body.syncGroup || null;
            const syncRules = getSyncRules();

            if (!uploadedFile) {
                return res.status(400).json({ error: 'Файл не загружен' });
            }
            if (!filePath || filePath === '/') {
                return res.status(400).json({ error: 'Не указан путь файла' });
            }

            const fileBuffer = await fs.readFile(uploadedFile.path);
            const fileHash = crypto.createHash('sha256').update(fileBuffer).digest('hex');
            const now = Date.now();
            const userRoot = await getUserRoot(db, config, userId);
            const { syncStatus, syncGroupId: resolvedGroupId } = await resolveUploadStatusForUser(
                db,
                userId,
                filePath,
                syncRules,
                syncGroupId
            );

            const existingFile = await db.get(
                'SELECT * FROM user_files WHERE user_id = ? AND path = ? AND is_deleted = 0',
                userId,
                filePath
            );

            const fileId = existingFile?.id || uuidv4();
            const storagePath = path.join(userRoot, fileId);
            await fs.move(uploadedFile.path, storagePath, { overwrite: true });

            if (existingFile) {
                await db.run(
                    `UPDATE user_files
                     SET hash = ?, size = ?, modified_time = ?, version = version + 1,
                         updated_at = ?, sync_status = ?, sync_group_id = ?
                     WHERE user_id = ? AND path = ?`,
                    fileHash,
                    uploadedFile.size,
                    now,
                    now,
                    syncStatus,
                    resolvedGroupId,
                    userId,
                    filePath
                );
            } else {
                await db.run(
                    `INSERT INTO user_files
                     (id, user_id, path, name, size, hash, modified_time, version,
                      is_deleted, parent_path, sync_group_id, sync_status, created_at, updated_at)
                     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
                    fileId,
                    userId,
                    filePath,
                    path.basename(filePath),
                    uploadedFile.size,
                    fileHash,
                    now,
                    1,
                    0,
                    path.posix.dirname(filePath),
                    resolvedGroupId,
                    syncStatus,
                    now,
                    now
                );
            }

            const action = syncStatus === 'ready' ? 'upload' : 'waiting';
            await db.run(
                `INSERT INTO file_changes (id, user_id, path, action, hash, size, timestamp, synced)
                 VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
                uuidv4(),
                userId,
                filePath,
                action,
                fileHash,
                uploadedFile.size,
                now,
                1
            );

            if (syncStatus === 'ready' && wsServer) {
                wsServer.broadcastUserFileChange(userId, {
                    action: 'upload',
                    path: filePath,
                    hash: fileHash,
                    size: uploadedFile.size,
                    timestamp: now
                });
                await releaseWaitingFiles(db, config, userId, syncRules, wsServer);
            }

            res.json({
                success: true,
                file_id: fileId,
                path: filePath,
                hash: fileHash,
                size: uploadedFile.size,
                sync_status: syncStatus
            });
        } catch (error) {
            console.error('Ошибка загрузки:', error);
            res.status(500).json({ error: 'Ошибка загрузки файла' });
        }
    };

    const downloadFile = async (req, res) => {
        const filePath = normalizePath(req.params[0] || '');
        const userId = req.user.userId;

        try {
            const file = await db.get(
                'SELECT * FROM user_files WHERE user_id = ? AND path = ? AND is_deleted = 0',
                userId,
                filePath
            );

            if (!file) {
                return res.status(404).json({ error: 'Файл не найден' });
            }
            if (file.sync_status === 'waiting') {
                return res.status(425).json({
                    error: 'Файл ожидает загрузки других файлов из группы',
                    group_id: file.sync_group_id
                });
            }

            const userRoot = await getUserRoot(db, config, userId);
            const storagePath = path.join(userRoot, file.id);
            if (!await fs.pathExists(storagePath)) {
                return res.status(404).json({ error: 'Файл не найден на диске' });
            }

            res.download(storagePath, file.name);
        } catch (error) {
            console.error('Ошибка скачивания:', error);
            res.status(500).json({ error: 'Ошибка скачивания файла' });
        }
    };

    const deleteFile = async (req, res) => {
        const filePath = normalizePath(req.params[0] || '');
        const userId = req.user.userId;

        try {
            await db.run(
                'UPDATE user_files SET is_deleted = 1, updated_at = ? WHERE user_id = ? AND path = ?',
                Date.now(),
                userId,
                filePath
            );

            const now = Date.now();
            await db.run(
                `INSERT INTO file_changes (id, user_id, path, action, timestamp, synced)
                 VALUES (?, ?, ?, ?, ?, ?)`,
                uuidv4(),
                userId,
                filePath,
                'delete',
                now,
                1
            );

            if (wsServer) {
                wsServer.broadcastUserFileChange(userId, {
                    action: 'delete',
                    path: filePath,
                    timestamp: now
                });
            }

            res.json({ success: true });
        } catch (error) {
            console.error('Ошибка удаления:', error);
            res.status(500).json({ error: 'Ошибка удаления файла' });
        }
    };

    const listFiles = async (req, res) => {
        const folderPath = normalizePath(req.query.path || '/');
        const userId = req.user.userId;

        try {
            const files = await db.all(
                `SELECT id, path, name, size, hash, modified_time, version, sync_status
                 FROM user_files
                 WHERE user_id = ? AND parent_path = ? AND is_deleted = 0
                 ORDER BY name`,
                userId,
                folderPath
            );
            res.json({ path: folderPath, files });
        } catch (error) {
            console.error('Ошибка получения списка файлов:', error);
            res.status(500).json({ error: 'Ошибка получения списка файлов' });
        }
    };

    app.get('/api/files/changes', authenticate, getChanges);
    app.get('/api/files/check', authenticate, checkFile);
    app.get('/api/files/list', authenticate, listFiles);
    app.post('/api/files/upload', authenticate, upload.single('file'), uploadFile);
    app.get('/api/files/download/*', authenticate, downloadFile);
    app.delete('/api/files/delete/*', authenticate, deleteFile);
}

module.exports = {
    registerFileRoutes,
    getUserRoot,
    normalizePath,
    fileExistsForUser,
    resolveUploadStatusForUser,
    releaseWaitingFiles
};
