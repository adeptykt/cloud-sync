using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CloudSyncShared;

namespace CloudSyncService;

public class WebSocketClient
{
    private readonly SyncConfig _config;
    private readonly Action<string> _logger;
    private ClientWebSocket _webSocket;
    private CancellationTokenSource _cts;
    private bool _isConnecting;
    private int _reconnectAttempts;
    private readonly int _maxReconnectAttempts = 10;
    private readonly int _reconnectDelay = 5000;

    public bool IsConnected { get; private set; }
    public event Action<string> OnMessage;
    public event Action OnConnected;
    public event Action OnDisconnected;

    public WebSocketClient(SyncConfig config, Action<string> logger)
    {
        _config = config;
        _logger = logger;
        _webSocket = new ClientWebSocket();
    }

    public async Task ConnectAsync()
    {
        try
        {
            // Подключаемся к WebSocket
            var uri = new Uri($"ws://{_config.ServerUrl.Replace("http://", "").Replace("https://", "")}:{_config.WebSocketPort ?? 3001}");
            await _webSocket.ConnectAsync(uri, _cts.Token);
            
            // Отправляем аутентификацию
            var authMessage = new
            {
                type = "auth",
                token = _authToken,
                deviceId = Environment.MachineName
            };
            
            await SendMessageAsync(JsonSerializer.Serialize(authMessage));
            
            IsConnected = true;
            _logger("WebSocket подключён и аутентифицирован");
            OnConnected?.Invoke();
            
            _ = Task.Run(ReceiveMessages);
        }
        catch (Exception ex)
        {
            _logger($"Ошибка подключения WebSocket: {ex.Message}");
            await ReconnectAsync();
        }
    }

    // Адаптация путей для пользователя
    private string GetUserRelativePath(string fullPath)
    {
        // Путь относительно корневой папки пользователя
        var userRoot = _config.SyncFolder; // Например: C:\Users\username\CloudSync
        return "/" + Path.GetRelativePath(userRoot, fullPath).Replace('\\', '/');
    }

    public async Task DisconnectAsync()
    {
        if (_webSocket == null) return;

        try
        {
            IsConnected = false;
            _cts?.Cancel();
            
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            
            _webSocket.Dispose();
            _webSocket = new ClientWebSocket();
            
            _logger("WebSocket отключён");
            OnDisconnected?.Invoke();
        }
        catch (Exception ex)
        {
            _logger($"Ошибка отключения WebSocket: {ex.Message}");
        }
    }

    private async Task ReceiveMessages()
    {
        var buffer = new byte[4096];
        
        while (IsConnected && _webSocket.State == WebSocketState.Open)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger("WebSocket закрыт сервером");
                    IsConnected = false;
                    OnDisconnected?.Invoke();
                    await ReconnectAsync();
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _logger($"Получено сообщение: {message.Length} байт");
                    OnMessage?.Invoke(message);
                }
            }
            catch (OperationCanceledException)
            {
                // Ожидаемая отмена
                break;
            }
            catch (Exception ex)
            {
                _logger($"Ошибка приёма WebSocket сообщения: {ex.Message}");
                IsConnected = false;
                OnDisconnected?.Invoke();
                await ReconnectAsync();
                break;
            }
        }
    }

    public async Task SendMessageAsync(string message)
    {
        if (!IsConnected || _webSocket.State != WebSocketState.Open)
        {
            _logger("Не удалось отправить сообщение: WebSocket не подключён");
            return;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                _cts.Token);
        }
        catch (Exception ex)
        {
            _logger($"Ошибка отправки WebSocket сообщения: {ex.Message}");
        }
    }

    public async Task SendFileChangeAsync(FileChange change)
    {
        try
        {
            var json = JsonSerializer.Serialize(change);
            await SendMessageAsync(json);
        }
        catch (Exception ex)
        {
            _logger($"Ошибка отправки FileChange: {ex.Message}");
        }
    }

    private async Task ReconnectAsync()
    {
        if (_reconnectAttempts >= _maxReconnectAttempts)
        {
            _logger($"Достигнут лимит попыток переподключения ({_maxReconnectAttempts})");
            return;
        }

        _reconnectAttempts++;
        _logger($"Попытка переподключения {_reconnectAttempts}/{_maxReconnectAttempts} через {_reconnectDelay}мс...");
        
        await Task.Delay(_reconnectDelay);
        await ConnectAsync();
    }

    public async Task PingAsync()
    {
        if (!IsConnected) return;

        try
        {
            var pingMessage = JsonSerializer.Serialize(new { type = "ping", timestamp = DateTime.Now });
            await SendMessageAsync(pingMessage);
        }
        catch (Exception ex)
        {
            _logger($"Ошибка отправки Ping: {ex.Message}");
        }
    }
}