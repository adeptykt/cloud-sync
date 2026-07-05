const express = require('express');
const multer = require('multer');
const http = require('http');
const https = require('https');
const fs = require('fs-extra');
const path = require('path');
const { v4: uuidv4 } = require('uuid');
const cors = require('cors');
const morgan = require('morgan');
const rateLimit = require('express-rate-limit');
const config = require('./config');
const { open } = require('sqlite');
const sqlite3 = require('sqlite3');
const jwt = require('jsonwebtoken');
const bcrypt = require('bcrypt');
const WebSocketServer = require('./websocket-server');
const { registerFileRoutes } = require('./file-api');

const app = express();
const PORT = config.port;

if (config.trustProxy) {
    app.set('trust proxy', 1);
}

app.use(cors());
app.use(morgan(config.nodeEnv === 'production' ? 'combined' : 'dev'));
app.use(express.json());

fs.ensureDirSync(config.storage.files);
fs.ensureDirSync(config.storage.temp);

let syncRules = config.loadSyncRules();
const getSyncRules = () => syncRules;

function reloadSyncRules() {
    syncRules = config.loadSyncRules();
    return syncRules;
}

let db;
let wsServer;
let serverStartedAt = Date.now();

const upload = multer({
    dest: config.storage.temp,
    limits: { fileSize: config.maxFileSize }
});

const authRateLimiter = rateLimit({
    windowMs: config.rateLimit.authWindowMs,
    max: config.rateLimit.authMax,
    standardHeaders: true,
    legacyHeaders: false,
    message: { error: 'Слишком много попыток, повторите позже' }
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

app.get('/api/health', async (req, res) => {
    let dbOk = false;
    if (db) {
        try {
            await db.get('SELECT 1');
            dbOk = true;
        } catch {
            dbOk = false;
        }
    }

    const wsStats = wsServer ? wsServer.getStats() : null;
    const healthy = dbOk;
    res.status(healthy ? 200 : 503).json({
        status: healthy ? 'ok' : 'degraded',
        uptime_seconds: Math.floor(process.uptime()),
        started_at: serverStartedAt,
        database: dbOk ? 'connected' : 'error',
        websocket: wsStats
            ? { port: config.wsPort, clients: wsStats.totalClients }
            : { port: config.wsPort, clients: 0 },
        rules_groups: syncRules.orderGroups?.length || 0
    });
});

app.post('/api/auth/register', authRateLimiter, async (req, res) => {
    try {
        const { username, password } = req.body;
        if (!username || !password) {
            return res.status(400).json({ error: 'Укажите username и password' });
        }
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

app.post('/api/auth/login', authRateLimiter, async (req, res) => {
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

app.get('/api/sync/rules', authenticate, (req, res) => {
    res.json({
        ...getSyncRules(),
        timestamp: Date.now()
    });
});

app.post('/api/sync/rules/reload', authenticate, (req, res) => {
    const rules = reloadSyncRules();
    if (wsServer) {
        wsServer.broadcastSyncRules(rules);
    }
    res.json({ success: true, order_groups: rules.orderGroups?.length || 0 });
});

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

function warnInsecureProductionConfig() {
    if (config.nodeEnv !== 'production') return;

    if (config.jwtSecret === config.defaultJwtSecret) {
        console.warn('⚠️  JWT_SECRET не задан — используется небезопасное значение по умолчанию');
    }
    if (!config.ssl.enabled) {
        console.warn('⚠️  SSL не настроен — для production используйте SSL_CERT_PATH/SSL_KEY_PATH или reverse proxy');
    }
}

function createHttpServer() {
    if (config.ssl.enabled) {
        const credentials = {
            cert: fs.readFileSync(config.ssl.certPath),
            key: fs.readFileSync(config.ssl.keyPath)
        };
        return https.createServer(credentials, app);
    }
    return http.createServer(app);
}

async function startServer() {
    await initDatabase();
    warnInsecureProductionConfig();

    wsServer = new WebSocketServer();
    wsServer.start();

    registerFileRoutes(app, { db, config, getSyncRules, upload, authenticate, wsServer });

    const server = createHttpServer();
    server.listen(PORT, () => {
        const protocol = config.ssl.enabled ? 'https' : 'http';
        const wsScheme = config.ssl.enabled ? 'wss' : 'ws';
        console.log(`
    ═══════════════════════════════════════════════════════════
    ☁️  Cloud Sync Server (${config.nodeEnv})
    ═══════════════════════════════════════════════════════════
    📡 HTTP API:        ${protocol}://localhost:${PORT}
    🔌 WebSocket:       ${wsScheme}://localhost:${config.wsPort}
    ❤️  Health:          ${protocol}://localhost:${PORT}/api/health
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
