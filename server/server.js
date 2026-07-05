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
const { registerFileRoutes } = require('./file-api');

const app = express();
const PORT = config.port || 3000;

app.use(cors());
app.use(morgan('combined'));
app.use(express.json());

fs.ensureDirSync(config.storage.files);
fs.ensureDirSync(config.storage.temp);

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

let db;
let wsServer;

const upload = multer({
    dest: config.storage.temp,
    limits: { fileSize: config.maxFileSize }
});

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
            root_folder TEXT,
            created_at INTEGER,
            last_login INTEGER,
            is_active INTEGER DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS user_files (
            id TEXT PRIMARY KEY,
            user_id TEXT,
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
            updated_at INTEGER,
            FOREIGN KEY (user_id) REFERENCES users(id)
        );

        CREATE TABLE IF NOT EXISTS sync_groups (
            id TEXT PRIMARY KEY,
            user_id TEXT,
            rule_id TEXT,
            group_name TEXT,
            files TEXT,
            status TEXT,
            created_at INTEGER,
            completed_at INTEGER,
            FOREIGN KEY (user_id) REFERENCES users(id)
        );

        CREATE TABLE IF NOT EXISTS sync_queue (
            id TEXT PRIMARY KEY,
            user_id TEXT,
            group_id TEXT,
            file_path TEXT,
            file_hash TEXT,
            priority INTEGER,
            status TEXT,
            retry_count INTEGER DEFAULT 0,
            created_at INTEGER,
            processed_at INTEGER,
            FOREIGN KEY (user_id) REFERENCES users(id)
        );

        CREATE TABLE IF NOT EXISTS file_changes (
            id TEXT PRIMARY KEY,
            user_id TEXT,
            path TEXT,
            action TEXT,
            hash TEXT,
            size INTEGER,
            timestamp INTEGER,
            synced INTEGER DEFAULT 0,
            FOREIGN KEY (user_id) REFERENCES users(id)
        );

        CREATE UNIQUE INDEX IF NOT EXISTS idx_user_files_path ON user_files(user_id, path);
        CREATE INDEX IF NOT EXISTS idx_user_files_user ON user_files(user_id);
        CREATE INDEX IF NOT EXISTS idx_file_changes_user_synced ON file_changes(user_id, synced);
        CREATE INDEX IF NOT EXISTS idx_file_changes_timestamp ON file_changes(user_id, timestamp);
    `);

    console.log('✓ База данных инициализирована');
}

app.post('/api/auth/register', async (req, res) => {
    try {
        const { username, password } = req.body;
        const hashedPassword = await bcrypt.hash(password, 10);
        const userId = uuidv4();
        const now = Date.now();

        await db.run(
            'INSERT INTO users (id, username, password_hash, created_at, is_active) VALUES (?, ?, ?, ?, 1)',
            userId,
            username,
            hashedPassword,
            now
        );

        res.json({ success: true, user_id: userId });
    } catch (error) {
        res.status(400).json({ error: 'Пользователь уже существует' });
    }
});

app.post('/api/auth/login', async (req, res) => {
    const { username, password } = req.body;
    const user = await db.get('SELECT * FROM users WHERE username = ? AND is_active = 1', username);

    if (!user || !(await bcrypt.compare(password, user.password_hash))) {
        return res.status(401).json({ error: 'Неверные учётные данные' });
    }

    await db.run('UPDATE users SET last_login = ? WHERE id = ?', Date.now(), user.id);

    const token = jwt.sign({ userId: user.id, username }, config.jwtSecret, { expiresIn: '30d' });
    res.json({
        token,
        user_id: user.id,
        username: user.username
    });
});

function authenticate(req, res, next) {
    const token = req.headers.authorization?.split(' ')[1];
    if (!token) {
        return res.status(401).json({ error: 'Требуется аутентификация' });
    }

    try {
        req.user = jwt.verify(token, config.jwtSecret);
        next();
    } catch (error) {
        res.status(401).json({ error: 'Неверный токен' });
    }
}

app.get('/api/user/info', authenticate, async (req, res) => {
    const userId = req.user.userId;

    try {
        const user = await db.get(
            'SELECT id, username, root_folder, created_at, last_login FROM users WHERE id = ?',
            userId
        );

        if (!user) {
            return res.status(404).json({ error: 'Пользователь не найден' });
        }

        const stats = await db.get(
            `SELECT COUNT(*) as total_files, SUM(size) as total_size
             FROM user_files WHERE user_id = ? AND is_deleted = 0`,
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

app.get('/api/stats', authenticate, async (req, res) => {
    const totalFiles = await db.get('SELECT COUNT(*) as count FROM user_files WHERE is_deleted = 0');
    const totalSize = await db.get('SELECT SUM(size) as size FROM user_files WHERE is_deleted = 0');
    const wsStats = wsServer ? wsServer.getStats() : { totalClients: 0 };

    res.json({
        files: totalFiles?.count || 0,
        totalSize: totalSize?.size || 0,
        wsClients: wsStats.totalClients || 0,
        authenticatedClients: wsStats.authenticatedClients || 0,
        uptime: process.uptime()
    });
});

async function startServer() {
    await initDatabase();

    wsServer = new WebSocketServer();
    wsServer.start();

    registerFileRoutes(app, { db, config, syncRules, upload, authenticate, wsServer });

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
    ═══════════════════════════════════════════════════════════
        `);
    });
}

startServer().catch(console.error);

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
