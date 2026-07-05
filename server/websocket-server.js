const WebSocket = require('ws');
const jwt = require('jsonwebtoken');
const { v4: uuidv4 } = require('uuid');
const fs = require('fs-extra');
const path = require('path');
const { open } = require('sqlite');
const sqlite3 = require('sqlite3');
const config = require('./config');

class WebSocketServer {
    constructor(port) {
        this.port = port || config.wsPort || 3001;
        this.clients = new Map(); // clientId -> { ws, userId, deviceId, lastPing }
        this.userSessions = new Map(); // userId -> Set of clientIds
        this.rooms = new Map(); // roomId -> Set of clientIds
        this.db = null;
        this.wss = null;
        this.pingInterval = null;
        
        this.authenticatedClients = new Map(); // clientId -> { userId, deviceId, username }
        this.initDatabase();
    }

    async initDatabase() {
        try {
            await fs.ensureDir(path.dirname(config.database));
            this.db = await open({
                filename: config.database,
                driver: sqlite3.Database
            });
            
            // Создаём таблицы с поддержкой пользователей
            await this.db.exec(`
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
                
                CREATE UNIQUE INDEX idx_user_files_path ON user_files(user_id, path);
                CREATE INDEX idx_user_files_user ON user_files(user_id);
                CREATE INDEX idx_file_changes_user_synced ON file_changes(user_id, synced);
            `);
            
            console.log('✓ WebSocket: База данных с поддержкой пользователей инициализирована');
        } catch (error) {
            console.error('✗ WebSocket: Ошибка подключения к БД:', error);
        }
    }

    start() {
        this.wss = new WebSocket.Server({ 
            port: this.port,
            clientTracking: true,
            perMessageDeflate: false
        });

        this.wss.on('connection', (ws, req) => {
            const clientId = uuidv4();
            const ip = req.socket.remoteAddress;
            
            console.log(`🔌 WebSocket клиент подключён: ${clientId} (IP: ${ip})`);
            
            this.clients.set(clientId, {
                ws: ws,
                authenticated: false,
                userId: null,
                deviceId: null,
                username: null,
                connectedAt: Date.now(),
                lastPing: Date.now()
            });

            this.sendToClient(clientId, {
                type: 'welcome',
                clientId: clientId,
                timestamp: Date.now(),
                message: 'Подключение к серверу установлено. Требуется авторизация.'
            });

            ws.on('message', async (data) => {
                try {
                    const message = JSON.parse(data.toString());
                    await this.handleMessage(clientId, message);
                } catch (error) {
                    console.error(`✗ Ошибка обработки сообщения от ${clientId}:`, error);
                    this.sendToClient(clientId, {
                        type: 'error',
                        error: 'Неверный формат сообщения',
                        details: error.message
                    });
                }
            });

            ws.on('close', (code, reason) => {
                console.log(`🔌 Клиент отключён: ${clientId} (${code})`);
                this.handleDisconnect(clientId);
            });

            ws.on('error', (error) => {
                console.error(`✗ Ошибка WebSocket для ${clientId}:`, error);
            });

            ws.on('pong', () => {
                const client = this.clients.get(clientId);
                if (client) {
                    client.lastPing = Date.now();
                }
            });
        });

        this.pingInterval = setInterval(() => {
            this.checkClientsHealth();
        }, 30000);

        setInterval(() => {
            this.cleanupStaleClients();
        }, 60000);

        console.log(`🔌 WebSocket сервер запущен на порту ${this.port}`);
        console.log(`   Ожидание подключений...`);
    }

    async handleMessage(clientId, message) {
        const client = this.clients.get(clientId);
        if (!client) return;

        console.log(`📨 [${client.username || clientId}] Сообщение: ${message.type || 'unknown'}`);

        switch (message.type) {
            case 'auth':
                await this.handleAuth(clientId, message);
                break;

            case 'ping':
                this.handlePing(clientId, message);
                break;

            case 'subscribe':
                await this.handleSubscribe(clientId, message);
                break;

            case 'unsubscribe':
                await this.handleUnsubscribe(clientId, message);
                break;

            case 'file_change':
                await this.handleFileChange(clientId, message);
                break;

            case 'sync':
                await this.handleSync(clientId, message);
                break;

            case 'get_changes':
                await this.handleGetChanges(clientId, message);
                break;

            case 'file_downloaded':
                await this.handleFileDownloaded(clientId, message);
                break;

            default:
                this.sendToClient(clientId, {
                    type: 'error',
                    error: 'Неизвестный тип сообщения',
                    messageType: message.type
                });
        }
    }

    // ============= АУТЕНТИФИКАЦИЯ С ПОДДЕРЖКОЙ ПОЛЬЗОВАТЕЛЕЙ =============

    async handleAuth(clientId, message) {
        const { token, deviceId } = message;
        const client = this.clients.get(clientId);
        
        try {
            // Проверяем JWT токен
            const decoded = jwt.verify(token, config.jwtSecret);
            
            // Получаем пользователя из БД
            const user = await this.db.get(
                `SELECT id, username, root_folder, is_active 
                 FROM users 
                 WHERE id = ? AND is_active = 1`,
                decoded.userId
            );
            
            if (!user) {
                throw new Error('Пользователь не найден или неактивен');
            }

            // Создаём корневую папку пользователя, если её нет
            const userRoot = user.root_folder || path.join(config.storage.files, 'users', user.id);
            if (!user.root_folder) {
                await this.db.run(
                    'UPDATE users SET root_folder = ? WHERE id = ?',
                    userRoot, user.id
                );
            }
            await fs.ensureDir(userRoot);

            // Авторизуем клиента
            client.authenticated = true;
            client.userId = user.id;
            client.username = user.username;
            client.deviceId = deviceId || uuidv4();
            client.rootFolder = userRoot;
            
            this.authenticatedClients.set(clientId, {
                userId: user.id,
                deviceId: client.deviceId,
                username: user.username,
                rootFolder: userRoot
            });

            // Добавляем в сессии пользователя
            if (!this.userSessions.has(user.id)) {
                this.userSessions.set(user.id, new Set());
            }
            this.userSessions.get(user.id).add(clientId);

            // Обновляем время последнего входа
            await this.db.run(
                'UPDATE users SET last_login = ? WHERE id = ?',
                Date.now(), user.id
            );

            this.sendToClient(clientId, {
                type: 'auth_success',
                userId: user.id,
                deviceId: client.deviceId,
                username: user.username,
                rootFolder: userRoot,
                timestamp: Date.now()
            });

            console.log(`✅ Пользователь ${user.username} (${user.id}) авторизован`);
            
            // Отправляем правила синхронизации пользователя
            await this.sendSyncRules(clientId);
            
            // Отправляем последние изменения пользователя
            await this.sendLatestChanges(clientId);

        } catch (error) {
            console.error(`✗ Ошибка аутентификации ${clientId}:`, error.message);
            this.sendToClient(clientId, {
                type: 'auth_failed',
                error: error.message || 'Ошибка аутентификации'
            });
            
            setTimeout(() => {
                const client = this.clients.get(clientId);
                if (client && client.ws) {
                    client.ws.close(1008, 'Authentication failed');
                }
            }, 1000);
        }
    }

    // ============= ОБРАБОТКА ФАЙЛОВЫХ СОБЫТИЙ С УЧЁТОМ ПОЛЬЗОВАТЕЛЯ =============

    async handleFileChange(clientId, message) {
        const client = this.clients.get(clientId);
        if (!client || !client.authenticated) return;

        const { change } = message;
        
        try {
            // Сохраняем изменение с привязкой к пользователю
            await this.saveUserFileChange(client.userId, change);
            
            // Рассылаем изменение только пользователям, у которых есть доступ
            this.broadcastUserFileChange(client.userId, change, clientId);
            
            console.log(`📤 [${client.username}] Распространено изменение: ${change.action} - ${change.path}`);
            
        } catch (error) {
            console.error(`✗ Ошибка сохранения изменения:`, error);
        }
    }

    async saveUserFileChange(userId, change) {
        const now = Date.now();
        
        if (change.action === 'upload') {
            // Проверяем существование файла у пользователя
            const existing = await this.db.get(
                'SELECT * FROM user_files WHERE user_id = ? AND path = ?',
                userId, change.path
            );
            
            if (existing) {
                await this.db.run(
                    `UPDATE user_files 
                     SET hash = ?, size = ?, modified_time = ?, 
                         version = version + 1, updated_at = ?
                     WHERE user_id = ? AND path = ?`,
                    change.hash, change.size, change.timestamp, now, userId, change.path
                );
            } else {
                await this.db.run(
                    `INSERT INTO user_files 
                     (id, user_id, path, name, size, hash, modified_time, 
                      version, is_deleted, sync_status, created_at, updated_at)
                     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
                    uuidv4(), userId, change.path, path.basename(change.path), 
                    change.size, change.hash, change.timestamp, 1, 0, 'ready', now, now
                );
            }
        } else if (change.action === 'delete') {
            await this.db.run(
                `UPDATE user_files SET is_deleted = 1, updated_at = ? 
                 WHERE user_id = ? AND path = ?`,
                now, userId, change.path
            );
        }
        
        // Сохраняем в историю изменений
        await this.db.run(
            `INSERT INTO file_changes (id, user_id, path, action, hash, size, timestamp, synced)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
            uuidv4(), userId, change.path, change.action, 
            change.hash || null, change.size || 0, now, 1
        );
    }

    async handleGetChanges(clientId, message) {
        const client = this.clients.get(clientId);
        if (!client || !client.authenticated) return;

        const { since = 0 } = message;
        
        try {
            // Получаем изменения только для этого пользователя
            const changes = await this.db.all(
                `SELECT * FROM user_files 
                 WHERE user_id = ? 
                 AND updated_at > ? 
                 AND sync_status != 'waiting'
                 ORDER BY updated_at ASC`,
                client.userId, since
            );
            
            this.sendToClient(clientId, {
                type: 'changes',
                changes: changes,
                current_time: Date.now(),
                count: changes.length
            });
            
            console.log(`📤 [${client.username}] Отправлено ${changes.length} изменений`);
            
        } catch (error) {
            console.error(`✗ Ошибка получения изменений:`, error);
        }
    }

    // ============= РАССЫЛКА С УЧЁТОМ ПОЛЬЗОВАТЕЛЕЙ =============

    broadcastUserFileChange(userId, change, excludeClientId = null) {
        const message = {
            type: 'file_change',
            change: change,
            timestamp: Date.now()
        };
        
        const messageStr = JSON.stringify(message);
        let sentCount = 0;
        
        // Получаем все сессии пользователя
        const userSessions = this.userSessions.get(userId);
        if (!userSessions) {
            console.log(`⚠️ Нет активных сессий для пользователя ${userId}`);
            return 0;
        }
        
        for (const clientId of userSessions) {
            if (clientId === excludeClientId) continue;
            const client = this.clients.get(clientId);
            if (!client || !client.authenticated) continue;
            if (client.ws.readyState !== WebSocket.OPEN) continue;
            
            try {
                client.ws.send(messageStr);
                sentCount++;
            } catch (error) {
                console.error(`✗ Ошибка отправки клиенту ${clientId}:`, error);
            }
        }
        
        if (sentCount > 0) {
            console.log(`📤 Изменение разослано ${sentCount} клиентам пользователя ${userId}`);
        }
        
        return sentCount;
    }

    sendToClient(clientId, data) {
        const client = this.clients.get(clientId);
        if (!client || !client.ws) return false;
        if (client.ws.readyState !== WebSocket.OPEN) return false;
        
        try {
            client.ws.send(JSON.stringify(data));
            return true;
        } catch (error) {
            console.error(`✗ Ошибка отправки клиенту ${clientId}:`, error);
            return false;
        }
    }

    // ============= ПРАВИЛА СИНХРОНИЗАЦИИ (общие для всех) =============

    async sendSyncRules(clientId) {
        try {
            const rulesPath = path.join(__dirname, 'sync-rules.json');
            if (await fs.pathExists(rulesPath)) {
                const rules = await fs.readJson(rulesPath);
                this.sendToClient(clientId, {
                    type: 'sync_rules',
                    rules: rules,
                    timestamp: Date.now()
                });
            }
        } catch (error) {
            console.error(`✗ Ошибка загрузки правил:`, error);
        }
    }

    async sendLatestChanges(clientId) {
        const client = this.clients.get(clientId);
        if (!client || !client.authenticated) return;
        
        try {
            const changes = await this.db.all(
                `SELECT * FROM user_files 
                 WHERE user_id = ? 
                 AND is_deleted = 0 
                 AND sync_status = 'ready'
                 ORDER BY updated_at DESC 
                 LIMIT 50`,
                client.userId
            );
            
            this.sendToClient(clientId, {
                type: 'latest_changes',
                changes: changes,
                count: changes.length,
                timestamp: Date.now()
            });
        } catch (error) {
            console.error(`✗ Ошибка отправки последних изменений:`, error);
        }
    }

    // ============= УПРАВЛЕНИЕ СОЕДИНЕНИЯМИ =============

    handleDisconnect(clientId) {
        const client = this.clients.get(clientId);
        if (!client) return;
        
        // Удаляем из сессий пользователя
        if (client.userId && this.userSessions.has(client.userId)) {
            const sessions = this.userSessions.get(client.userId);
            sessions.delete(clientId);
            if (sessions.size === 0) {
                this.userSessions.delete(client.userId);
                console.log(`👤 Пользователь ${client.username} отключился (нет активных сессий)`);
            }
        }
        
        // Удаляем из комнат
        for (const [room, clients] of this.rooms) {
            if (clients.has(clientId)) {
                clients.delete(clientId);
                if (clients.size === 0) {
                    this.rooms.delete(room);
                }
            }
        }
        
        this.clients.delete(clientId);
        this.authenticatedClients.delete(clientId);
        
        console.log(`📋 Клиент ${clientId} удалён`);
    }

    checkClientsHealth() {
        const now = Date.now();
        const timeout = 60000;
        
        for (const [clientId, client] of this.clients) {
            if (now - client.lastPing > timeout) {
                console.log(`⚠️ Клиент ${clientId} не отвечает (таймаут)`);
                
                this.sendToClient(clientId, {
                    type: 'ping',
                    timestamp: now
                });
                
                if (client.ws.readyState === WebSocket.OPEN) {
                    client.ws.terminate();
                }
                
                this.handleDisconnect(clientId);
            }
        }
    }

    cleanupStaleClients() {
        const now = Date.now();
        const maxAge = 2 * 60 * 60 * 1000;
        
        for (const [clientId, client] of this.clients) {
            if (!client.authenticated && now - client.connectedAt > maxAge) {
                console.log(`🧹 Удаление старого неавторизованного клиента: ${clientId}`);
                if (client.ws.readyState === WebSocket.OPEN) {
                    client.ws.close(1000, 'Session expired');
                }
                this.handleDisconnect(clientId);
            }
        }
    }

    // ============= СТАТИСТИКА =============

    getStats() {
        const authenticated = Array.from(this.clients.values())
            .filter(c => c.authenticated).length;
        
        return {
            totalClients: this.clients.size,
            authenticatedClients: authenticated,
            unauthenticatedClients: this.clients.size - authenticated,
            activeUsers: this.userSessions.size,
            rooms: this.rooms.size,
            uptime: process.uptime()
        };
    }

    stop() {
        if (this.pingInterval) {
            clearInterval(this.pingInterval);
        }
        
        for (const [clientId, client] of this.clients) {
            try {
                if (client.ws.readyState === WebSocket.OPEN) {
                    client.ws.close(1000, 'Server shutting down');
                }
            } catch (error) {
                console.error(`✗ Ошибка закрытия ${clientId}:`, error);
            }
        }
        
        this.wss.close(() => {
            console.log('🔌 WebSocket сервер остановлен');
        });
    }
}

module.exports = WebSocketServer;