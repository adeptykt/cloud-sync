using System.Diagnostics;
using System.ServiceProcess;
using CloudSyncShared;
using System.Text.Json;

namespace CloudSyncService;

public class SyncService : ServiceBase
{
    private SyncEngine _syncEngine;
    private FileWatcher _fileWatcher;
    private WebSocketClient _webSocketClient;
    private readonly string _configPath;
    private SyncConfig _config;
    private System.Timers.Timer _healthCheckTimer;

    public SyncService()
    {
        ServiceName = "CloudSyncAgent";
        CanStop = true;
        CanPauseAndContinue = true;
        AutoLog = true;
        
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CloudSyncAgent", "config.json");
    }

    protected override void OnStart(string[] args)
    {
        try
        {
            Log("Запуск службы CloudSyncAgent...");
            
            // Создаём папку для конфига и логов
            var appDataPath = Path.GetDirectoryName(_configPath);
            Directory.CreateDirectory(appDataPath);
            
            // Загружаем конфиг
            _config = LoadConfig();
            Log($"Конфигурация загружена. Папка синхронизации: {_config.SyncFolder}");
            
            // Инициализируем WebSocket клиент
            _webSocketClient = new WebSocketClient(_config, Log);
            _webSocketClient.OnMessage += HandleWebSocketMessage;
            _webSocketClient.OnConnected += () => Log("WebSocket подключён к серверу");
            _webSocketClient.OnDisconnected += () => Log("WebSocket отключён от сервера");
            
            // Инициализируем движок синхронизации
            _syncEngine = new SyncEngine(_config, Log, _webSocketClient);
            
            // Инициализируем FileWatcher
            _fileWatcher = new FileWatcher(_config, Log);
            _fileWatcher.OnFileCreated += async (path) => await _syncEngine.OnFileCreated(path);
            _fileWatcher.OnFileChanged += async (path) => await _syncEngine.OnFileChanged(path);
            _fileWatcher.OnFileDeleted += async (path) => await _syncEngine.OnFileDeleted(path);
            _fileWatcher.OnFileRenamed += async (oldPath, newPath) => await _syncEngine.OnFileRenamed(oldPath, newPath);
            
            // Запускаем все компоненты
            _webSocketClient.ConnectAsync().Wait();
            _syncEngine.Start();
            _fileWatcher.Start();
            
            // Health check
            _healthCheckTimer = new System.Timers.Timer(60000); // Каждую минуту
            _healthCheckTimer.Elapsed += (s, e) => HealthCheck();
            _healthCheckTimer.Start();
            
            Log($"Служба {ServiceName} успешно запущена");
        }
        catch (Exception ex)
        {
            Log($"Ошибка запуска службы: {ex.Message}");
            Log($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    protected override void OnStop()
    {
        Log("Остановка службы CloudSyncAgent...");
        
        _healthCheckTimer?.Stop();
        _fileWatcher?.Stop();
        _syncEngine?.Stop();
        _webSocketClient?.DisconnectAsync().Wait();
        
        Log($"Служба {ServiceName} остановлена");
    }

    protected override void OnPause()
    {
        Log("Служба приостановлена");
        _fileWatcher?.Stop();
        _syncEngine?.Stop();
    }

    protected override void OnContinue()
    {
        Log("Служба возобновлена");
        _fileWatcher?.Start();
        _syncEngine?.Start();
    }

    private SyncConfig LoadConfig()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<SyncConfig>(json) ?? new SyncConfig();
            }
            catch (Exception ex)
            {
                Log($"Ошибка загрузки конфига: {ex.Message}");
                return new SyncConfig();
            }
        }
        
        // Создаём конфиг по умолчанию
        var defaultConfig = new SyncConfig
        {
            ServerUrl = "http://localhost:3000",
            Username = "admin",
            Password = "admin123",
            SyncFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CloudSync"),
            SyncIntervalSeconds = 5,
            StartWithWindows = true,
            ShowNotifications = true,
            MaxRetryCount = 3
        };
        
        SaveConfig(defaultConfig);
        return defaultConfig;
    }

    private void SaveConfig(SyncConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Log($"Ошибка сохранения конфига: {ex.Message}");
        }
    }

    private void HealthCheck()
    {
        try
        {
            var status = "OK";
            if (!_webSocketClient?.IsConnected ?? true)
                status = "WebSocket отключён";
            
            Log($"Health Check: {status}, SyncFolder: {_config.SyncFolder}, Queue: {_syncEngine?.GetQueueCount() ?? 0}");
        }
        catch (Exception ex)
        {
            Log($"Health Check ошибка: {ex.Message}");
        }
    }

    private void HandleWebSocketMessage(string message)
    {
        try
        {
            var change = JsonSerializer.Deserialize<FileChange>(message);
            if (change != null)
            {
                Log($"WebSocket сообщение: {change.Action} - {change.Path}");
                // Обработка входящих изменений
                _ = Task.Run(() => _syncEngine.ApplyServerChange(change));
            }
        }
        catch (Exception ex)
        {
            Log($"Ошибка обработки WebSocket сообщения: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        var logPath = Path.Combine(
            Path.GetDirectoryName(_configPath),
            $"service_{DateTime.Now:yyyy-MM-dd}.log");
        
        var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}";
        
        try
        {
            File.AppendAllText(logPath, logEntry + Environment.NewLine);
        }
        catch { /* Игнорируем ошибки логирования */ }
        
        Debug.WriteLine(logEntry);
        
        // Если есть консоль (для отладки)
        Console.WriteLine(logEntry);
    }
}