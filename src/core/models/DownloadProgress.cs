namespace XastDownloader.Core.Models;

public class DownloadProgress
{
    public string TaskId { get; set; } = string.Empty;
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public double Speed { get; set; } // bytes per second
    public TimeSpan EstimatedTimeRemaining { get; set; }
    public double ProgressPercentage { get; set; }
    public int ActiveConnections { get; set; }
}
