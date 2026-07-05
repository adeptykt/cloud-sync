const path = require('path');

module.exports = {
    port: process.env.PORT || 3000,
    wsPort: process.env.WS_PORT || 3001,
    storage: {
        files: path.join(__dirname, 'storage', 'files'),
        temp: path.join(__dirname, 'storage', 'temp')
    },
    database: path.join(__dirname, 'database', 'sync.db'),
    maxFileSize: 100 * 1024 * 1024, // 100MB
    jwtSecret: process.env.JWT_SECRET || 'your-secret-key-change-this',
    syncRules: path.join(__dirname, 'sync-rules.json')
};