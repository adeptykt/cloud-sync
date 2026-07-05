using System.Collections.Concurrent;
using CloudSyncShared;

namespace CloudSyncService;

public class FileWatcher
{
    private readonly SyncConfig _config;
    private readonly Action<string> _logger;
    private FileSystemWatcher _watcher;
    private readonly ConcurrentDictionary<string, DateTime> _pendingFiles;
    private readonly int _delayMilliseconds = 1500; // Ожидание завершения записи
    private bool _isRunning;

    public event Func<string, Task> OnFileCreated;
    public event Func<string, Task> OnFileChanged;
    public event Func<string, Task> OnFileDeleted;
    public event Func<string, string, Task> OnFileRenamed;

    public FileWatcher(SyncConfig config, Action<string> logger)
    {
        _config = config;
        _logger = logger;
        _pendingFiles = new ConcurrentDictionary<string, DateTime>();
    }

    public void Start()
    {
        if (_isRunning) return;

        try
        {
            // Создаём папку, если её нет
            if (!Directory.Exists(_config.SyncFolder))
            {
                Directory.CreateDirectory(_config.SyncFolder);
                _logger($"Создана папка синхронизации: {_config.SyncFolder}");
            }

            _watcher = new FileSystemWatcher(_config.SyncFolder)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | 
                              NotifyFilters.LastWrite | 
                              NotifyFilters.Size | 
                              NotifyFilters.CreationTime
            };

            // Подписываемся на события
            _watcher.Created += OnCreated;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnDeleted;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;

            _isRunning = true;
            _logger($"FileWatcher запущен для папки: {_config.SyncFolder}");

            // Запускаем фоновую обработку отложенных файлов
            Task.Run(ProcessPendingFiles);
        }
        catch (Exception ex)
        {
            _logger($"Ошибка запуска FileWatcher: {ex.Message}");
            throw;
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _watcher?.Dispose();
        _pendingFiles.Clear();
        _logger("FileWatcher остановлен");
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnore(e.FullPath)) return;

        _logger($"Обнаружен созданный файл: {e.FullPath}");
        
        // Добавляем в очередь ожидания
        _pendingFiles[e.FullPath] = DateTime.Now;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnore(e.FullPath)) return;

        _logger($"Обнаружен изменённый файл: {e.FullPath}");
        
        // Обновляем время изменения
        _pendingFiles[e.FullPath] = DateTime.Now;
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnore(e.FullPath)) return;

        _logger($"Обнаружен удалённый файл: {e.FullPath}");
        
        _pendingFiles.TryRemove(e.FullPath, out _);
        
        // Вызываем событие удаления
        OnFileDeleted?.Invoke(e.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (ShouldIgnore(e.FullPath)) return;

        _logger($"Обнаружено переименование: {e.OldFullPath} -> {e.FullPath}");
        
        _pendingFiles.TryRemove(e.OldFullPath, out _);
        _pendingFiles[e.FullPath] = DateTime.Now;
        
        // Вызываем событие переименования
        OnFileRenamed?.Invoke(e.OldFullPath, e.FullPath);
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        var exception = e.GetException();
        _logger($"Ошибка FileWatcher: {exception.Message}");
    }

    private async Task ProcessPendingFiles()
    {
        while (_isRunning)
        {
            try
            {
                var now = DateTime.Now;
                var readyFiles = _pendingFiles
                    .Where(kvp => (now - kvp.Value).TotalMilliseconds > _delayMilliseconds)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var filePath in readyFiles)
                {
                    if (_pendingFiles.TryRemove(filePath, out _))
                    {
                        if (File.Exists(filePath))
                        {
                            // Проверяем, не открыт ли файл другим процессом
                            if (IsFileReady(filePath))
                            {
                                await OnFileChanged?.Invoke(filePath);
                            }
                            else
                            {
                                // Файл ещё используется - возвращаем в очередь
                                _pendingFiles[filePath] = DateTime.Now;
                            }
                        }
                    }
                }

                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                _logger($"Ошибка в ProcessPendingFiles: {ex.Message}");
            }
        }
    }

    private bool ShouldIgnore(string path)
    {
        // Игнорируем временные файлы и системные
        var fileName = Path.GetFileName(path);
        return fileName.StartsWith("~") ||
               fileName.StartsWith(".") ||
               fileName.StartsWith("$") ||
               fileName.EndsWith(".tmp") ||
               fileName.EndsWith(".temp") ||
               fileName.EndsWith(".lock") ||
               path.Contains("\\~") ||
               path.Contains("\\.sync");
    }

    private bool IsFileReady(string path)
    {
        try
        {
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                return true;
            }
        }
        catch (IOException)
        {
            return false;
        }
    }
}