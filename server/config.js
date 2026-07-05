require('dotenv').config();

const path = require('path');
const fs = require('fs');

const defaultJwtSecret = 'your-secret-key-change-this';

function loadSyncRulesFile(syncRulesPath) {
    try {
        return JSON.parse(fs.readFileSync(syncRulesPath, 'utf8'));
    } catch {
        return {
            orderGroups: [],
            defaultBehavior: 'immediate',
            conflictResolution: 'newest_wins'
        };
    }
}

module.exports = {
    port: Number(process.env.PORT) || 3000,
    wsPort: Number(process.env.WS_PORT) || 3001,
    nodeEnv: process.env.NODE_ENV || 'development',
    trustProxy: process.env.TRUST_PROXY === 'true',
    storage: {
        files: path.join(__dirname, 'storage', 'files'),
        temp: path.join(__dirname, 'storage', 'temp')
    },
    database: path.join(__dirname, 'database', 'sync.db'),
    maxFileSize: Number(process.env.MAX_FILE_SIZE_MB || 100) * 1024 * 1024,
    jwtSecret: process.env.JWT_SECRET || defaultJwtSecret,
    defaultJwtSecret,
    syncRulesPath: path.join(__dirname, 'sync-rules.json'),
    loadSyncRules: () => loadSyncRulesFile(path.join(__dirname, 'sync-rules.json')),
    ssl: {
        enabled: Boolean(process.env.SSL_CERT_PATH && process.env.SSL_KEY_PATH),
        certPath: process.env.SSL_CERT_PATH || '',
        keyPath: process.env.SSL_KEY_PATH || ''
    },
    rateLimit: {
        authWindowMs: Number(process.env.AUTH_RATE_LIMIT_WINDOW_MS || 15 * 60 * 1000),
        authMax: Number(process.env.AUTH_RATE_LIMIT_MAX || 20)
    }
};
