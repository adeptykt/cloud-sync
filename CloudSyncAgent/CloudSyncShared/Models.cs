using System.Text.Json.Serialization;

namespace CloudSyncShared;

public class SyncConfig
{
    public string ServerUrl { get; set; } = "http://localhost:3000";
    public int WebSocketPort { get; set; } = 3001;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string SyncFolder { get; set; } = @"C:\CloudSync";
    public int SyncIntervalSeconds { get; set; } = 5;
    public bool StartWithWindows { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public int MaxRetryCount { get; set; } = 3;
    public List<SyncRule> CustomRules { get; set; } = new();
}

public class SyncRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string FilePattern { get; set; } = "";
    public SyncOrderType OrderType { get; set; } = SyncOrderType.DataBeforeFlag;
    public List<string> SequentialOrder { get; set; } = new();
    public string Description { get; set; } = "";
}

public enum SyncOrderType
{
    Immediate,
    DataBeforeFlag,
    Sequential
}

public class FileMetadata
{
    public string Id { get; set; }
    public string Path { get; set; }
    public string Name { get; set; }
    public long Size { get; set; }
    public string Hash { get; set; }
    public long ModifiedTime { get; set; }
    public int Version { get; set; }
    public bool IsDeleted { get; set; }
    public string SyncStatus { get; set; }
    public string SyncGroupId { get; set; }
}

public class FileChange
{
    public string Action { get; set; } // upload, delete, rename, ready
    public string Path { get; set; }
    public string OldPath { get; set; }
    public string Hash { get; set; }
    public long Size { get; set; }
    public long Timestamp { get; set; }
}

public class SyncQueueItem
{
    public string Id { get; set; }
    public string GroupId { get; set; }
    public string FilePath { get; set; }
    public string LocalPath { get; set; }
    public int Priority { get; set; }
    public string Status { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
}