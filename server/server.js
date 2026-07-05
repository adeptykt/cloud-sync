const express = require('express');
const multer = require('multer');
const { v4: uuidv4 } = require('uuid');
const cors = require('cors');
const morgan = require('morgan');
const fs = require('fs-extra');
const path = require('path');
const config = require('./config');
const { open } = require('sqlite');
const sqlite3 = require('sqlite3');
const crypto = require('crypto');
const jwt = require('jsonwebtoken');
const bcrypt = require('bcrypt');
const WebSocketServer = require('./websocket-server');

const app = express();
const PORT = config.port || 3000;

// Middleware
app.use(cors());
app.use(morgan('combined'));
app.use(express.json());

// Инициализация хранилищ
fs.ensureDirSync(config.storage.files);
fs.ensureDirSync(config.storage.temp);

// Загрузка правил синхронизации
let syncRules = {};
try {
    syncRules = require(config.syncRules);
} catch (error) {
    console.warn('⚠️ Правила синхронизации не найдены, используются стандартные');
    syncRules = {
        orderGroups: [],
        defaultBehavior: 'immediate',
        conflictResolution: 'newest_wins'
    };
}

// База данных
let db;

// WebSocket сервер
let wsServer;

// Настройка multer
const upload = multer({
    dest: config.storage.temp,
    limits: { fileSize: config.maxFileSize }
});

// ============= ИНИЦИАЛИЗАЦИЯ БД =============

async function initDatabase() {
    await fs.ensureDir(path.dirname(config.database));
    db = await open({
        filename: config.database,
        driver: sqlite3.Database
    });
    
    await db.exec(`
        CREATE TABLE IF NOT EXISTS users (
            id TEXT PRIMARY KEY,
            username TEXT UNIQUE,
            password_hash TEXT,
            created_at INTEGER
        );
        
        CREATE TABLE IF NOT EXISTS files (
            id TEXT PRIMARY KEY,
            path TEXT,
            name TEXT,
            size INTEGER,
            hash TEXT,
            modified_time INTEGER,
            version INTEGER,
            is_deleted INTEGER DEFAULT 0,
            parent_path TEXT,
            sync_group_id TEXT,
            sync_status TEXT,
            created_at INTEGER,
            updated_at INTEGER
        );
        
        CREATE TABLE IF NOT EXISTS sync_groups (
            id TEXT PRIMARY KEY,
            rule_id TEXT,
            group_name TEXT,
            files TEXT,
            status TEXT,
            created_at INTEGER,
            completed_at INTEGER
        );
        
        CREATE TABLE IF NOT EXISTS sync_queue (
            id TEXT PRIMARY KEY,
            group_id TEXT,
            file_path TEXT,
            file_hash TEXT,
            priority INTEGER,
            status TEXT,
            retry_count INTEGER DEFAULT 0,
            created_at INTEGER,
            processed_at INTEGER
        );
        
        CREATE TABLE IF NOT EXISTS file_changes (
            id TEXT PRIMARY KEY,
            path TEXT,
            action TEXT,
            hash TEXT,
            size INTEGER,
            user_id TEXT,
            timestamp INTEGER,
            synced INTEGER DEFAULT 0
        );
        
        CREATE INDEX IF NOT EXISTS idx_files_path ON files(path);
        CREATE INDEX IF NOT EXISTS idx_files_sync_group ON files(sync_group_id);
        CREATE INDEX IF NOT EXISTS idx_sync_queue_status ON sync_queue(status);
        CREATE INDEX IF NOT EXISTS idx_file_changes_synced ON file_changes(synced);
    `);
    
    console.log('✓ База данных инициализирована');
}

// ============= АУТЕНТИФИКАЦИЯ =============

app.post('/api/auth/register', async (req, res) => {
    try {
        const { username, password } = req.body;
        const hashedPassword = await bcrypt.hash(password, 10);
        const userId = uuidv4();
        
        await db.run(
            'INSERT INTO users (id, username, password_hash, created_at) VALUES (?, ?, ?, ?)',
            userId, username, hashedPassword, Date.now()
        );
        
        res.json({ success: true, user_id: userId });
    } catch (error) {
        res.status(400).json({ error: 'Пользователь уже существует' });
    }
});

app.post('/api/auth/login', async (req, res) => {
    const { username, password } = req.body;
    const user = await db.get('SELECT * FROM users WHERE username = ?', username);
    
    if (!user || !(await bcrypt.compare(password, user.password_hash))) {
        return res.status(401).json({ error: 'Неверные учётные данные' });
    }
    
    const token = jwt.sign({ userId: user.id, username }, config.jwtSecret, { expiresIn: '30d' });
    res.json({ 
        token, 
        user_id: user.id,
        username: user.username
    });
});

// Middleware аутентификации
function authenticate(req, res, next) {
    const token = req.headers['authorization']?.split(' ')[1];
    if (!token) {
        return res.status(401).json({ error: 'Требуется аутентификация' });
    }
    
    try {
        const decoded = jwt.verify(token, config.jwtSecret);
        req.user = decoded;
        next();
    } catch (error) {
        res.status(401).json({ error: 'Неверный токен' });
    }
}

// ============= API ЭНДПОИНТЫ =============

/**
 * Удалить файл
 */
app.delete('/api/files/delete/*', authenticate, async (req, res) => {
    const filePath = '/' + req.params[0];
    
    await db.run(
        'UPDATE files SET is_deleted = 1, updated_at = ? WHERE path = ?',
        Date.now(), filePath
    );
    
    // Сохраняем в историю
    await db.run(
        `INSERT INTO file_changes (id, path, action, user_id, timestamp, synced)
         VALUES (?, ?, ?, ?, ?, ?)`,
        uuidv4(), filePath, 'delete', req.user.userId, Date.now(), 1
    );
    
    // Уведомляем через WebSocket
    wsServer.broadcastFileChange({
        action: 'delete',
        path: filePath,
        timestamp: Date.now()
    });
    
    res.json({ success: true });
});

/**
 * Получить список файлов пользователя
 */
app.get('/api/user/files', authenticate, async (req, res) => {
    const { path: folderPath = '/' } = req.query;
    const userId = req.user.userId;
    
    try {
        const files = await db.all(
            `SELECT * FROM user_files 
             WHERE user_id = ? 
             AND parent_path = ? 
             AND is_deleted = 0 
             ORDER BY name`,
            userId, folderPath
        );
        
        res.json({
            path: folderPath,
            files: files.map(f => ({
                id: f.id,
                name: f.name,
                path: f.path,
                size: f.size,
                modified_time: f.modified_time,
                version: f.version,
                sync_status: f.sync_status
            }))
        });
    } catch (error) {
        console.error('Ошибка получения списка файлов:', error);
        res.status(500).json({ error: 'Ошибка получения списка файлов' });
    }
});

/**
 * Получить изменения пользователя с определённого времени
 */
app.get('/api/user/changes', authenticate, async (req, res) => {
    const { since = 0 } = req.query;
    const userId = req.user.userId;
    
    try {
        const changes = await db.all(
            `SELECT * FROM user_files 
             WHERE user_id = ? 
             AND updated_at > ? 
             AND sync_status != 'waiting'
             ORDER BY updated_at ASC`,
            userId, since
        );
        
        res.json({
            changes: changes,
            current_time: Date.now(),
            user_id: userId
        });
    } catch (error) {
        console.error('Ошибка получения изменений:', error);
        res.status(500).json({ error: 'Ошибка получения изменений' });
    }
});

/**
 * Загрузить файл пользователя
 */
app.post('/api/user/upload', authenticate, upload.single('file'), async (req, res) => {
    try {
        const { filePath } = req.body;
        const uploadedFile = req.file;
        const userId = req.user.userId;
        
        if (!uploadedFile) {
            return res.status(400).json({ error: 'Файл не загружен' });
        }
        
        // Получаем корневую папку пользователя
        const user = await db.get(
            'SELECT root_folder FROM users WHERE id = ?',
            userId
        );
        
        if (!user) {
            return res.status(404).json({ error: 'Пользователь не найден' });
        }
        
        // Вычисляем хеш
        const fileBuffer = await fs.readFile(uploadedFile.path);
        const fileHash = crypto.createHash('sha256').update(fileBuffer).digest('hex');
        
        const now = Date.now();
        const fileId = uuidv4();
        
        // Сохраняем файл в папку пользователя
        const userRoot = user.root_folder || path.join(config.storage.files, 'users', userId);
        await fs.ensureDir(userRoot);
        
        const storagePath = path.join(userRoot, fileId);
        await fs.move(uploadedFile.path, storagePath);
        
        // Проверяем существующий файл пользователя
        const existingFile = await db.get(
            'SELECT * FROM user_files WHERE user_id = ? AND path = ? AND is_deleted = 0',
            userId, filePath
        );
        
        if (existingFile) {
            await db.run(
                `UPDATE user_files 
                 SET hash = ?, size = ?, modified_time = ?, version = version + 1, 
                     updated_at = ?, sync_status = ?
                 WHERE user_id = ? AND path = ?`,
                fileHash, uploadedFile.size, now, now, 'ready', userId, filePath
            );
        } else {
            await db.run(
                `INSERT INTO user_files 
                 (id, user_id, path, name, size, hash, modified_time, version, 
                  is_deleted, parent_path, sync_status, created_at, updated_at)
                 VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
                fileId, userId, filePath, path.basename(filePath), 
                uploadedFile.size, fileHash, now, 1, 0, 
                path.dirname(filePath), 'ready', now, now
            );
        }
        
        // Сохраняем в историю
        await db.run(
            `INSERT INTO file_changes (id, user_id, path, action, hash, size, timestamp, synced)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
            uuidv4(), userId, filePath, 'upload', fileHash, uploadedFile.size, now, 1
        );
        
        // Уведомляем через WebSocket
        if (wsServer) {
            wsServer.broadcastUserFileChange(userId, {
                action: 'upload',
                path: filePath,
                hash: fileHash,
                size: uploadedFile.size,
                timestamp: now
            });
        }
        
        res.json({
            success: true,
            file_id: fileId,
            path: filePath,
            hash: fileHash,
            size: uploadedFile.size
        });
        
    } catch (error) {
        console.error('Ошибка загрузки:', error);
        res.status(500).json({ error: 'Ошибка загрузки файла' });
    }
});

/**
 * Скачать файл пользователя
 */
app.get('/api/user/download/*', authenticate, async (req, res) => {
    const filePath = '/' + req.params[0];
    const userId = req.user.userId;
    
    try {
        const file = await db.get(
            'SELECT * FROM user_files WHERE user_id = ? AND path = ? AND is_deleted = 0',
            userId, filePath
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
        
        // Получаем корневую папку пользователя
        const user = await db.get(
            'SELECT root_folder FROM users WHERE id = ?',
            userId
        );
        
        if (!user) {
            return res.status(404).json({ error: 'Пользователь не найден' });
        }
        
        const storagePath = path.join(user.root_folder, file.id);
        if (!await fs.pathExists(storagePath)) {
            return res.status(404).json({ error: 'Файл не найден на диске' });
        }
        
        res.download(storagePath, file.name);
        
    } catch (error) {
        console.error('Ошибка скачивания:', error);
        res.status(500).json({ error: 'Ошибка скачивания файла' });
    }
});

/**
 * Удалить файл пользователя
 */
app.delete('/api/user/delete/*', authenticate, async (req, res) => {
    const filePath = '/' + req.params[0];
    const userId = req.user.userId;
    
    try {
        await db.run(
            'UPDATE user_files SET is_deleted = 1, updated_at = ? WHERE user_id = ? AND path = ?',
            Date.now(), userId, filePath
        );
        
        await db.run(
            `INSERT INTO file_changes (id, user_id, path, action, timestamp, synced)
             VALUES (?, ?, ?, ?, ?, ?)`,
            uuidv4(), userId, filePath, 'delete', Date.now(), 1
        );
        
        if (wsServer) {
            wsServer.broadcastUserFileChange(userId, {
                action: 'delete',
                path: filePath,
                timestamp: Date.now()
            });
        }
        
        res.json({ success: true });
        
    } catch (error) {
        console.error('Ошибка удаления:', error);
        res.status(500).json({ error: 'Ошибка удаления файла' });
    }
});

/**
 * Получить информацию о пользователе
 */
app.get('/api/user/info', authenticate, async (req, res) => {
    const userId = req.user.userId;
    
    try {
        const user = await db.get(
            `SELECT id, username, root_folder, created_at, last_login 
             FROM users WHERE id = ?`,
            userId
        );
        
        if (!user) {
            return res.status(404).json({ error: 'Пользователь не найден' });
        }
        
        const stats = await db.get(
            `SELECT 
                COUNT(*) as total_files,
                SUM(size) as total_size
             FROM user_files 
             WHERE user_id = ? AND is_deleted = 0`,
            userId
        );
        
        res.json({
            user: {
                id: user.id,
                username: user.username,
                root_folder: user.root_folder,
                created_at: user.created_at,
                last_login: user.last_login
            },
            stats: {
                total_files: stats?.total_files || 0,
                total_size: stats?.total_size || 0,
                total_size_mb: Math.round((stats?.total_size || 0) / 1024 / 1024 * 100) / 100
            }
        });
        
    } catch (error) {
        console.error('Ошибка получения информации:', error);
        res.status(500).json({ error: 'Ошибка получения информации' });
    }
});

/**
 * Получить статистику
 */
app.get('/api/stats', authenticate, async (req, res) => {
    const totalFiles = await db.get('SELECT COUNT(*) as count FROM files WHERE is_deleted = 0');
    const totalSize = await db.get('SELECT SUM(size) as size FROM files WHERE is_deleted = 0');
    const wsStats = wsServer ? wsServer.getStats() : { totalClients: 0 };
    
    res.json({
        files: totalFiles?.count || 0,
        totalSize: totalSize?.size || 0,
        wsClients: wsStats.totalClients || 0,
        authenticatedClients: wsStats.authenticatedClients || 0,
        uptime: process.uptime()
    });
});

// ============= ЗАПУСК =============

async function startServer() {
    await initDatabase();
    
    // Запускаем WebSocket сервер
    wsServer = new WebSocketServer();
    wsServer.start();
    
    // Запускаем HTTP сервер
    app.listen(PORT, () => {
        console.log(`
    ═══════════════════════════════════════════════════════════
    ☁️  Облачный файловый сервис с WebSocket поддержкой
    ═══════════════════════════════════════════════════════════
    📡 HTTP сервер:     http://localhost:${PORT}
    🔌 WebSocket:       ws://localhost:${config.wsPort || 3001}
    💾 База данных:     ${config.database}
    📁 Хранилище:       ${config.storage.files}
    ⚙️  Правила:         ${syncRules.orderGroups?.length || 0} групп
    👥 Клиенты:         Подключено: 0
    ═══════════════════════════════════════════════════════════
        `);
    });
}

startServer().catch(console.error);

// Graceful shutdown
process.on('SIGTERM', () => {
    console.log('Получен SIGTERM, остановка сервера...');
    if (wsServer) wsServer.stop();
    process.exit(0);
});

process.on('SIGINT', () => {
    console.log('Получен SIGINT, остановка сервера...');
    if (wsServer) wsServer.stop();
    process.exit(0);
});