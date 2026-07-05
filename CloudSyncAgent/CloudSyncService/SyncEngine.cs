using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CloudSyncShared;

namespace CloudSyncService;

public class SyncEngine
{
    private readonly SyncConfig _config;
    private readonly Action<string> _logger;
    private readonly WebSocketClient _webSocket;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, SyncQueueItem> _syncQueue;
    private readonly ConcurrentDictionary<string, DateTime> _processingFiles;
    private readonly string _stateFilePath;
    private System.Timers.Timer _syncTimer;
    private bool _isRunning;
    private string _authToken;
    private long _lastSyncTime;

    public SyncEngine(SyncConfig config, Action<string> logger, WebSocketClient webSocket)
    {
        _config = config;
        _logger = logger;
        _webSocket = webSocket;
        _httpClient = new HttpClient();
        _syncQueue = new ConcurrentDictionary<string, SyncQueueItem>();
        _processingFiles = new ConcurrentDictionary<string, DateTime>();
        _stateFilePath = Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)))),
            "CloudSyncAgent", "sync_state.json");
        
        Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath));
        _lastSyncTime = LoadLastSyncTime();
    }

    public async void Start()
    {
        if (_isRunning) return;

        _isRunning = true;
        
        try
        {
            // Аутентификация
            await Authenticate();
            
            // Запускаем периодическую синхронизацию
            _syncTimer = new System.Timers.Timer(_config.SyncIntervalSeconds * 1000);
            _syncTimer.Elapsed += async (s, e) => await SyncChanges();
            _syncTimer.Start();
            
            // Первоначальная синхронизация
            await SyncChanges();
            
            _logger("SyncEngine запущен");
        }
        catch (Exception ex)
        {
            _logger($"Ошибка запуска SyncEngine: {ex.Message}");
            throw;
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _syncTimer?.Stop();
        _syncTimer?.Dispose();
        _logger("SyncEngine остановлен");
    }

    private async Task Authenticate()
    {
        try
        {
            var loginData = new
            {
                username = _config.Username,
                password = _config.Password
            };
            
            var json = JsonSerializer.Serialize(loginData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{_config.ServerUrl}/api/auth/login", content);
            
            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    await response.Content.ReadAsStringAsync());
                _authToken = result?["token"];
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authToken);
                _logger("Аутентификация успешна");
            }
            else
            {
                throw new Exception($"Ошибка аутентификации: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger($"Ошибка аутентификации: {ex.Message}");
            throw;
        }
    }

    private async Task SyncChanges()
    {
        if (!_isRunning || string.IsNullOrEmpty(_authToken)) return;

        try
        {
            _logger("Запуск синхронизации...");
            
            // 1. Получаем изменения с сервера
            var changes = await GetServerChanges();
            foreach (var change in changes)
            {
                await ApplyServerChange(change);
            }
            
            // 2. Обрабатываем локальные изменения из очереди
            await ProcessSyncQueue();
            
            // 3. Обновляем время последней синхронизации
            _lastSyncTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SaveLastSyncTime(_lastSyncTime);
            
            _logger($"Синхронизация завершена. Изменений: {changes.Count}, Очередь: {_syncQueue.Count}");
        }
        catch (Exception ex)
        {
            _logger($"Ошибка синхронизации: {ex.Message}");
        }
    }

    private async Task<List<FileChange>> GetServerChanges()
    {
        try
        {
            var url = $"{_config.ServerUrl}/api/files/changes?since={_lastSyncTime}";
            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                
                if (result != null && result.TryGetValue("changes", out var changesObj))
                {
                    var changesJson = changesObj.ToString();
                    return JsonSerializer.Deserialize<List<FileChange>>(changesJson) ?? new List<FileChange>();
                }
            }
        }
        catch (Exception ex)
        {
            _logger($"Ошибка получения изменений: {ex.Message}");
        }
        
        return new List<FileChange>();
    }

    public async Task ApplyServerChange(FileChange change)
    {
        try
        {
            var localPath = Path.Combine(_config.SyncFolder, change.Path.TrimStart('/'));
            var dir = Path.GetDirectoryName(localPath);
            
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            switch (change.Action)
            {
                case "upload":
                case "ready":
                    if (!File.Exists(localPath) || GetFileHash(localPath) != change.Hash)
                    {
                        await DownloadFile(change.Path, localPath);
                    }
                    break;
                    
                case "delete":
                    if (File.Exists(localPath))
                    {
                        File.Delete(localPath);
                        _logger($"Удалён файл: {localPath}");
                    }
                    break;
                    
                case "create_folder":
                    if (!Directory.Exists(localPath))
                    {
                        Directory.CreateDirectory(localPath);
                        _logger($"Создана папка: {localPath}");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger($"Ошибка применения изменения {change.Path}: {ex.Message}");
        }
    }

    private async Task DownloadFile(string serverPath, string localPath)
    {
        try
        {
            var url = $"{_config.ServerUrl}/api/files/download{serverPath}";
            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                // Скачиваем во временный файл
                var tempPath = localPath + ".tmp";
                using (var fileStream = File.Create(tempPath))
                {
                    await response.Content.CopyToAsync(fileStream);
                }
                
                // Перемещаем в конечное место
                if (File.Exists(localPath))
                    File.Delete(localPath);
                File.Move(tempPath, localPath);
                
                _logger($"Скачан файл: {localPath}");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger($"Файл не найден на сервере: {serverPath}");
            }
            else
            {
                _logger($"Ошибка скачивания {localPath}: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger($"Ошибка скачивания {localPath}: {ex.Message}");
        }
    }

    private async Task ProcessSyncQueue()
    {
        var waitingItems = _syncQueue.Values
            .Where(x => x.Status == "waiting")
            .OrderBy(x => x.Priority)
            .ToList();

        foreach (var item in waitingItems)
        {
            if (!_isRunning) break;
            
            // Проверяем, можно ли уже загрузить
            var canUpload = await CheckOrderRule(item.FilePath);
            
            if (canUpload)
            {
                if (_syncQueue.TryRemove(item.Id, out _))
                {
                    await UploadFile(item.LocalPath, item.FilePath);
                }
            }
        }
    }

    public async Task OnFileCreated(string localPath)
    {
        if (!_isRunning) return;
        
        var relativePath = GetRelativePath(localPath);
        
        // Игнорируем системные файлы
        if (ShouldIgnore(relativePath)) return;
        
        _logger($"Обнаружен новый файл: {relativePath}");
        
        // Проверяем правила порядка
        var canUpload = await CheckOrderRule(relativePath);
        
        if (canUpload)
        {
            await UploadFile(localPath, relativePath);
        }
        else
        {
            // Добавляем в очередь ожидания
            var queueItem = new SyncQueueItem
            {
                Id = Guid.NewGuid().ToString(),
                FilePath = relativePath,
                LocalPath = localPath,
                Priority = GetFilePriority(relativePath),
                Status = "waiting",
                CreatedAt = DateTime.Now,
                RetryCount = 0
            };
            
            _syncQueue.TryAdd(queueItem.Id, queueItem);
            _logger($"Файл добавлен в очередь ожидания: {relativePath}");
        }
    }

    public async Task OnFileChanged(string localPath)
    {
        if (!_isRunning) return;
        
        var relativePath = GetRelativePath(localPath);
        
        if (ShouldIgnore(relativePath)) return;
        
        _logger($"Обнаружено изменение файла: {relativePath}");
        
        // Проверяем, не обрабатываем ли мы этот файл уже
        if (_processingFiles.ContainsKey(localPath))
            return;
        
        _processingFiles.TryAdd(localPath, DateTime.Now);
        
        try
        {
            await OnFileCreated(localPath);
        }
        finally
        {
            _processingFiles.TryRemove(localPath, out _);
        }
    }

    public async Task OnFileDeleted(string localPath)
    {
        if (!_isRunning) return;
        
        var relativePath = GetRelativePath(localPath);
        
        if (ShouldIgnore(relativePath)) return;
        
        _logger($"Обнаружено удаление файла: {relativePath}");
        
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { path = relativePath }),
                Encoding.UTF8, "application/json");
            
            var response = await _httpClient.DeleteAsync($"{_config.ServerUrl}/api/files/delete{relativePath}");
            
            if (response.IsSuccessStatusCode)
            {
                _logger($"Удалён на сервере: {relativePath}");
            }
        }
        catch (Exception ex)
        {
            _logger($"Ошибка удаления {relativePath}: {ex.Message}");
        }
    }

    public async Task OnFileRenamed(string oldLocalPath, string newLocalPath)
    {
        if (!_isRunning) return;
        
        var oldRelativePath = GetRelativePath(oldLocalPath);
        var newRelativePath = GetRelativePath(newLocalPath);
        
        if (ShouldIgnore(oldRelativePath) || ShouldIgnore(newRelativePath)) return;
        
        _logger($"Обнаружено переименование: {oldRelativePath} -> {newRelativePath}");
        
        // Сначала загружаем новый файл
        await OnFileCreated(newLocalPath);
        
        // Через некоторое время удаляем старый
        await Task.Delay(1000);
        await OnFileDeleted(oldLocalPath);
    }

    private async Task UploadFile(string localPath, string relativePath)
    {
        try
        {
            if (!File.Exists(localPath))
            {
                _logger($"Файл не существует: {localPath}");
                return;
            }

            _logger($"Загрузка файла: {relativePath}");

            using var form = new MultipartFormDataContent();
            var fileBytes = await File.ReadAllBytesAsync(localPath);
            var fileContent = new ByteArrayContent(fileBytes);
            form.Add(fileContent, "file", Path.GetFileName(localPath));
            form.Add(new StringContent(relativePath), "filePath");
            
            // Проверяем правила
            var rule = GetSyncRule(relativePath);
            if (rule != null)
            {
                form.Add(new StringContent(rule.Id), "syncGroup");
                form.Add(new StringContent(GetFilePriority(relativePath).ToString()), "orderPriority");
            }
            
            var response = await _httpClient.PostAsync($"{_config.ServerUrl}/api/files/upload", form);
            
            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    await response.Content.ReadAsStringAsync());
                
                var syncStatus = result?.GetValueOrDefault("sync_status")?.ToString() ?? "ready";
                
                if (syncStatus == "waiting")
                {
                    // Добавляем в очередь
                    var queueItem = new SyncQueueItem
                    {
                        Id = result?["file_id"]?.ToString() ?? Guid.NewGuid().ToString(),
                        FilePath = relativePath,
                        LocalPath = localPath,
                        Priority = GetFilePriority(relativePath),
                        Status = "waiting",
                        CreatedAt = DateTime.Now
                    };
                    _syncQueue.TryAdd(queueItem.Id, queueItem);
                    _logger($"Файл поставлен в очередь: {relativePath}");
                }
                else
                {
                    _logger($"Загружен файл: {relativePath} (статус: {syncStatus})");
                    
                    // Отправляем уведомление через WebSocket
                    await _webSocket.SendFileChangeAsync(new FileChange
                    {
                        Action = "upload",
                        Path = relativePath,
                        Hash = GetFileHash(localPath),
                        Size = new FileInfo(localPath).Length,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    });
                }
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger($"Ошибка загрузки {relativePath}: {response.StatusCode} - {error}");
            }
        }
        catch (Exception ex)
        {
            _logger($"Ошибка загрузки {localPath}: {ex.Message}");
        }
    }

    private async Task<bool> CheckOrderRule(string filePath)
    {
        var rule = GetSyncRule(filePath);
        if (rule == null || !rule.Enabled) return true;
        
        if (rule.OrderType == SyncOrderType.DataBeforeFlag)
        {
            var isFlag = Path.GetExtension(filePath) == ".flag" || 
                        Path.GetExtension(filePath) == ".ready" ||
                        filePath.EndsWith(".flag") || 
                        filePath.EndsWith(".ready");
            
            if (isFlag)
            {
                // Проверяем существование файла данных
                var dataFile = filePath.Replace(".flag", "").Replace(".ready", "");
                var dataPath = Path.Combine(_config.SyncFolder, dataFile);
                
                if (!File.Exists(dataPath))
                {
                    // Проверяем на сервере
                    try
                    {
                        var response = await _httpClient.GetAsync(
                            $"{_config.ServerUrl}/api/files/check?path={dataFile}");
                        var exists = await response.Content.ReadAsStringAsync();
                        if (exists != "true")
                        {
                            return false;
                        }
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }
        
        if (rule.OrderType == SyncOrderType.Sequential && rule.SequentialOrder.Any())
        {
            var fileName = Path.GetFileName(filePath);
            var fileIndex = rule.SequentialOrder.IndexOf(fileName);
            
            if (fileIndex > 0)
            {
                // Проверяем, загружены ли предыдущие файлы
                for (int i = 0; i < fileIndex; i++)
                {
                    var prevFile = rule.SequentialOrder[i];
                    var prevPath = Path.Combine(Path.GetDirectoryName(filePath), prevFile);
                    
                    if (!File.Exists(prevPath))
                    {
                        return false;
                    }
                }
            }
        }
        
        return true;
    }

    private SyncRule GetSyncRule(string filePath)
    {
        // Сначала проверяем пользовательские правила
        var userRule = _config.CustomRules?.FirstOrDefault(r => 
            r.Enabled && 
            (r.FilePattern == "*" || 
             Path.GetFileName(filePath).Contains(r.FilePattern) ||
             filePath.EndsWith(r.FilePattern)));
        
        if (userRule != null) return userRule;
        
        // Стандартные правила
        if (filePath.EndsWith(".flag") || filePath.EndsWith(".ready"))
        {
            return new SyncRule
            {
                Id = "default-flag",
                Name = "Флаг по умолчанию",
                OrderType = SyncOrderType.DataBeforeFlag,
                Enabled = true,
                FilePattern = "*.flag,*.ready"
            };
        }
        
        return null;
    }

    private int GetFilePriority(string filePath)
    {
        var rule = GetSyncRule(filePath);
        if (rule == null) return 0;
        
        if (rule.OrderType == SyncOrderType.DataBeforeFlag)
        {
            if (filePath.EndsWith(".flag") || filePath.EndsWith(".ready"))
                return 10;
            return 0;
        }
        
        if (rule.OrderType == SyncOrderType.Sequential && rule.SequentialOrder.Any())
        {
            var fileName = Path.GetFileName(filePath);
            var index = rule.SequentialOrder.IndexOf(fileName);
            return index >= 0 ? index : 999;
        }
        
        return 0;
    }

    private string GetFileHash(string filePath)
    {
        try
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    private string GetRelativePath(string fullPath)
    {
        try
        {
            return "/" + Path.GetRelativePath(_config.SyncFolder, fullPath).Replace('\\', '/');
        }
        catch
        {
            return "/" + Path.GetFileName(fullPath);
        }
    }

    private bool ShouldIgnore(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.StartsWith("~") ||
               fileName.StartsWith(".") ||
               fileName.StartsWith("$") ||
               fileName.EndsWith(".tmp") ||
               fileName.EndsWith(".temp") ||
               fileName.EndsWith(".lock");
    }

    private long LoadLastSyncTime()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                var json = File.ReadAllText(_stateFilePath);
                var state = JsonSerializer.Deserialize<Dictionary<string, long>>(json);
                return state?.GetValueOrDefault("lastSync", 0) ?? 0;
            }
        }
        catch { /* Игнорируем */ }
        return 0;
    }

    private void SaveLastSyncTime(long time)
    {
        try
        {
            var state = new Dictionary<string, long> { ["lastSync"] = time };
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
        }
        catch { /* Игнорируем */ }
    }

    public int GetQueueCount()
    {
        return _syncQueue.Count;
    }
}