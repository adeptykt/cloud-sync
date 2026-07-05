# Cloud Sync

Облачная синхронизация файлов для Windows с поддержкой **упорядоченной загрузки** (data → flag, sequential). Подходит для интеграций с 1С, Frontol и другими системами обмена.

## Компоненты

| Компонент | Описание |
|-----------|----------|
| **CloudSyncService** | Windows-служба: отслеживание файлов, синхронизация, WebSocket |
| **CloudSyncTray** | Приложение в системном трее: настройки, лог, управление службой |
| **server** | Node.js сервер: REST API, SQLite, WebSocket |

## Быстрый старт

### Сервер

```bash
cd server
npm install
cp .env.example .env   # задайте JWT_SECRET
npm start
```

- HTTP: `http://localhost:3000`
- WebSocket: `ws://localhost:3001`

Регистрация первого пользователя:

```bash
curl -X POST http://localhost:3000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"your-password"}'
```

### Windows-агент

```bash
cd CloudSyncAgent
dotnet build CloudSync.sln -c Release
```

Конфигурация: `%ProgramData%\CloudSyncAgent\config.json`

Установка службы (от имени администратора):

```bat
Installer\Install.bat
```

## Правила синхронизации

**Единый источник правил — сервер:** `server/sync-rules.json`.

- Агент загружает правила после логина: `GET /api/sync/rules`
- Обновления рассылаются по WebSocket (`type: sync_rules`)
- Локальные `CustomRules` используются только если сервер недоступен

Типы правил:

- **data_before_flag** — сначала файл данных (`.dat`, `.csv`), затем флаг (`.flag`, `.ready`)
- **sequential** — строгий порядок файлов в группе

Проверка зависимостей на клиенте и сервере выполняется через `/api/files/check` (локально + на сервере).

## Структура репозитория

```
cloud-sync/
├── CloudSyncAgent/     # .NET 8 Windows-агент
│   ├── CloudSyncService/
│   ├── CloudSyncTray/
│   ├── CloudSyncShared/
│   └── Installer/
└── server/             # Node.js сервер
```

## API

Клиент использует эндпоинты `/api/files/*`:

| Метод | Путь | Описание |
|-------|------|----------|
| POST | `/api/auth/login` | Аутентификация |
| GET | `/api/files/changes?since=` | Изменения с timestamp |
| GET | `/api/files/check?path=` | Проверка наличия файла (`true`/`false`) |
| GET | `/api/files/list?path=` | Список файлов в папке |
| POST | `/api/files/upload` | Загрузка (`file`, `filePath`) |
| GET | `/api/files/download/*` | Скачивание |
| DELETE | `/api/files/delete/*` | Удаление |
| GET | `/api/sync/rules` | Правила порядка загрузки |
| GET | `/api/health` | Health check (без auth) |

## Production

1. Скопируйте `server/.env.example` → `server/.env`
2. Задайте `NODE_ENV=production` и длинный `JWT_SECRET`
3. HTTPS: `SSL_CERT_PATH` + `SSL_KEY_PATH` или reverse proxy (nginx/Caddy)
4. За reverse proxy: `TRUST_PROXY=true`
5. Проверка: `GET /api/health`

```bash
cd server
npm test
npm start
```

## Разработка

- .NET 8 SDK (Windows)
- Node.js 20+

CI запускается на push/PR в ветку `main`.

## Лицензия

MIT — см. [LICENSE](LICENSE).
