namespace XastDownloader.Core.Models;

public class Chunk
{
    public int Index { get; set; }
    public long StartByte { get; set; }
    public long EndByte { get; set; }
    public long DownloadedBytes { get; set; }
    public ChunkStatus Status { get; set; }
    public int RetryCount { get; set; }
    public string TempFilePath { get; set; } = string.Empty;
    
    public long TotalBytes => EndByte - StartByte + 1;
    public double Progress => TotalBytes > 0 ? (double)DownloadedBytes / TotalBytes * 100 : 0;
}

public enum ChunkStatus
{
    Pending,
    Downloading,
    Completed,
    Failed
}
