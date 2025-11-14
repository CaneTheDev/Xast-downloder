namespace XastDownloader.Core.Models;

public class DownloadTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string SavePath { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public DownloadStatus Status { get; set; }
    public int ConnectionCount { get; set; } = 16;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<Chunk> Chunks { get; set; } = new();
    
    public double Progress => TotalBytes > 0 ? (double)DownloadedBytes / TotalBytes * 100 : 0;
}

public enum DownloadStatus
{
    Pending,
    Downloading,
    Paused,
    Completed,
    Failed,
    Cancelled
}
