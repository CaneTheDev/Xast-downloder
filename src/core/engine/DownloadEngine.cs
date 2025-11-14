using System.Diagnostics;
using XastDownloader.Core.Models;
using XastDownloader.Core.Services;

namespace XastDownloader.Core.Engine;

public class DownloadEngine
{
    private readonly HttpService _httpService;
    private readonly string _tempDirectory;
    private readonly Dictionary<string, CancellationTokenSource> _cancellationTokens = new();
    private readonly Dictionary<string, DownloadTask> _activeTasks = new();
    private readonly Dictionary<string, Stopwatch> _speedWatches = new();
    private readonly Dictionary<string, long> _lastDownloadedBytes = new();
    private readonly Dictionary<string, Queue<double>> _speedHistory = new();
    private readonly Dictionary<string, DateTime> _lastUpdateTime = new();
    private const int SPEED_HISTORY_SIZE = 10;
    private const double UPDATE_INTERVAL_SECONDS = 0.5;

    public event EventHandler<DownloadProgress>? ProgressChanged;

    public DownloadEngine(string? tempDirectory = null)
    {
        _httpService = new HttpService();
        _tempDirectory = tempDirectory ?? Path.Combine(Path.GetTempPath(), "XastDownloader");
        Directory.CreateDirectory(_tempDirectory);
    }

    public async Task<DownloadTask> StartDownloadAsync(
        string url, 
        string savePath, 
        int connectionCount = 16)
    {
        Console.WriteLine($"[DownloadEngine] StartDownloadAsync called");
        Console.WriteLine($"[DownloadEngine] URL: {url}");
        Console.WriteLine($"[DownloadEngine] SavePath: {savePath}");
        Console.WriteLine($"[DownloadEngine] ConnectionCount: {connectionCount}");
        
        // Get file info
        Console.WriteLine($"[DownloadEngine] Getting file info...");
        var (supportsRanges, fileSize, fileName) = await _httpService.GetFileInfoAsync(url);
        Console.WriteLine($"[DownloadEngine] File info - SupportsRanges: {supportsRanges}, Size: {fileSize}, Name: {fileName}");

        if (fileSize == 0)
        {
            Console.WriteLine($"[DownloadEngine] ERROR: File size is 0");
            throw new Exception("Unable to determine file size");
        }

        // Create download task
        Console.WriteLine($"[DownloadEngine] Creating download task...");
        var task = new DownloadTask
        {
            Url = url,
            FileName = fileName ?? "download",
            SavePath = savePath,
            TotalBytes = fileSize,
            ConnectionCount = supportsRanges ? connectionCount : 1,
            Status = DownloadStatus.Downloading,
            StartedAt = DateTime.UtcNow
        };
        Console.WriteLine($"[DownloadEngine] Task ID: {task.Id}");
        Console.WriteLine($"[DownloadEngine] Effective connection count: {task.ConnectionCount}");

        // Create chunks
        Console.WriteLine($"[DownloadEngine] Creating chunks...");
        task.Chunks = ChunkManager.CreateChunks(fileSize, task.ConnectionCount);
        Console.WriteLine($"[DownloadEngine] Created {task.Chunks.Count} chunks");

        // Set temp file paths
        var taskTempDir = Path.Combine(_tempDirectory, task.Id);
        Console.WriteLine($"[DownloadEngine] Temp directory: {taskTempDir}");
        Directory.CreateDirectory(taskTempDir);

        foreach (var chunk in task.Chunks)
        {
            chunk.TempFilePath = Path.Combine(taskTempDir, $"chunk_{chunk.Index}.tmp");
        }
        Console.WriteLine($"[DownloadEngine] Temp file paths set for all chunks");

        // Start download
        Console.WriteLine($"[DownloadEngine] Initializing download tracking...");
        var cts = new CancellationTokenSource();
        _cancellationTokens[task.Id] = cts;
        _activeTasks[task.Id] = task;
        _speedWatches[task.Id] = Stopwatch.StartNew();
        _lastDownloadedBytes[task.Id] = 0;
        _speedHistory[task.Id] = new Queue<double>();
        _lastUpdateTime[task.Id] = DateTime.UtcNow;

        try
        {
            Console.WriteLine($"[DownloadEngine] Starting chunk downloads...");
            await DownloadChunksAsync(task, cts.Token);
            Console.WriteLine($"[DownloadEngine] All chunks downloaded successfully");
            
            // Merge chunks
            Console.WriteLine($"[DownloadEngine] Merging chunks...");
            await ChunkManager.MergeChunksAsync(task.Chunks, task.SavePath);
            Console.WriteLine($"[DownloadEngine] Chunks merged successfully");
            
            task.Status = DownloadStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            Console.WriteLine($"[DownloadEngine] Download completed successfully");
        }
        catch (OperationCanceledException ex)
        {
            Console.WriteLine($"[DownloadEngine] Download cancelled: {ex.Message}");
            task.Status = DownloadStatus.Cancelled;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DownloadEngine] ERROR during download: {ex.GetType().Name}");
            Console.WriteLine($"[DownloadEngine] ERROR Message: {ex.Message}");
            Console.WriteLine($"[DownloadEngine] ERROR StackTrace: {ex.StackTrace}");
            task.Status = DownloadStatus.Failed;
            throw;
        }
        finally
        {
            Console.WriteLine($"[DownloadEngine] Cleaning up task...");
            CleanupTask(task);
        }

        return task;
    }

    public void PauseDownload(string taskId)
    {
        Console.WriteLine($"[DownloadEngine] Pausing download: {taskId}");
        
        // Set status first to prevent race conditions
        if (_activeTasks.TryGetValue(taskId, out var task))
        {
            task.Status = DownloadStatus.Paused;
        }
        
        // Then cancel the token
        if (_cancellationTokens.TryGetValue(taskId, out var cts))
        {
            cts.Cancel();
        }
    }

    public async Task<DownloadTask> ResumeDownloadAsync(string taskId)
    {
        Console.WriteLine($"[DownloadEngine] Resuming download: {taskId}");
        
        if (!_activeTasks.TryGetValue(taskId, out var task))
        {
            throw new InvalidOperationException($"Task {taskId} not found");
        }

        if (task.Status != DownloadStatus.Paused)
        {
            throw new InvalidOperationException($"Task {taskId} is not paused");
        }

        // Check existing chunk files and update their status
        Console.WriteLine($"[DownloadEngine] Checking existing chunk files...");
        foreach (var chunk in task.Chunks)
        {
            if (File.Exists(chunk.TempFilePath))
            {
                var fileInfo = new FileInfo(chunk.TempFilePath);
                chunk.DownloadedBytes = fileInfo.Length;
                
                if (chunk.DownloadedBytes >= chunk.TotalBytes)
                {
                    chunk.Status = ChunkStatus.Completed;
                    Console.WriteLine($"[DownloadEngine] Chunk {chunk.Index} already completed ({chunk.DownloadedBytes} bytes)");
                }
                else
                {
                    chunk.Status = ChunkStatus.Pending;
                    Console.WriteLine($"[DownloadEngine] Chunk {chunk.Index} partially downloaded ({chunk.DownloadedBytes}/{chunk.TotalBytes} bytes)");
                }
            }
            else
            {
                chunk.DownloadedBytes = 0;
                chunk.Status = ChunkStatus.Pending;
                Console.WriteLine($"[DownloadEngine] Chunk {chunk.Index} not started");
            }
        }

        // Calculate total downloaded bytes
        task.DownloadedBytes = task.Chunks.Sum(c => c.DownloadedBytes);
        Console.WriteLine($"[DownloadEngine] Total downloaded: {task.DownloadedBytes}/{task.TotalBytes} bytes");

        // Create new cancellation token
        var cts = new CancellationTokenSource();
        _cancellationTokens[task.Id] = cts;
        _speedWatches[task.Id] = Stopwatch.StartNew();
        _lastDownloadedBytes[task.Id] = task.DownloadedBytes;
        _speedHistory[task.Id] = new Queue<double>();
        _lastUpdateTime[task.Id] = DateTime.UtcNow;

        task.Status = DownloadStatus.Downloading;

        try
        {
            Console.WriteLine($"[DownloadEngine] Resuming chunk downloads...");
            await DownloadChunksAsync(task, cts.Token);
            Console.WriteLine($"[DownloadEngine] All chunks downloaded successfully");
            
            // Merge chunks
            Console.WriteLine($"[DownloadEngine] Merging chunks...");
            await ChunkManager.MergeChunksAsync(task.Chunks, task.SavePath);
            Console.WriteLine($"[DownloadEngine] Chunks merged successfully");
            
            task.Status = DownloadStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            Console.WriteLine($"[DownloadEngine] Download completed successfully");
        }
        catch (OperationCanceledException ex)
        {
            Console.WriteLine($"[DownloadEngine] Download paused again: {ex.Message}");
            task.Status = DownloadStatus.Paused;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DownloadEngine] ERROR during resume: {ex.GetType().Name}");
            Console.WriteLine($"[DownloadEngine] ERROR Message: {ex.Message}");
            Console.WriteLine($"[DownloadEngine] ERROR StackTrace: {ex.StackTrace}");
            task.Status = DownloadStatus.Failed;
            throw;
        }
        finally
        {
            if (task.Status == DownloadStatus.Completed)
            {
                Console.WriteLine($"[DownloadEngine] Cleaning up completed task...");
                CleanupTask(task);
            }
        }

        return task;
    }

    public void CancelDownload(string taskId)
    {
        Console.WriteLine($"[DownloadEngine] Cancelling download: {taskId}");
        if (_cancellationTokens.TryGetValue(taskId, out var cts))
        {
            cts.Cancel();
        }
        
        if (_activeTasks.TryGetValue(taskId, out var task))
        {
            task.Status = DownloadStatus.Cancelled;
            CleanupTask(task);
        }
    }

    private void CleanupTask(DownloadTask task)
    {
        if (_speedWatches.TryGetValue(task.Id, out var watch))
        {
            watch.Stop();
            _speedWatches.Remove(task.Id);
        }
        _lastDownloadedBytes.Remove(task.Id);
        _speedHistory.Remove(task.Id);
        _lastUpdateTime.Remove(task.Id);
        _cancellationTokens.Remove(task.Id);

        if (task.Status == DownloadStatus.Completed || task.Status == DownloadStatus.Cancelled)
        {
            _activeTasks.Remove(task.Id);
            ChunkManager.CleanupTempFiles(task.Chunks);
            var taskTempDir = Path.Combine(_tempDirectory, task.Id);
            if (Directory.Exists(taskTempDir))
                Directory.Delete(taskTempDir, true);
        }
        // Keep task in _activeTasks if paused for resume
    }

    private async Task DownloadChunksAsync(DownloadTask task, CancellationToken cancellationToken)
    {
        var downloadTasks = task.Chunks.Select(chunk => 
            DownloadChunkWithProgressAsync(task, chunk, cancellationToken)
        );

        await Task.WhenAll(downloadTasks);
    }

    private async Task DownloadChunkWithProgressAsync(
        DownloadTask task, 
        Chunk chunk, 
        CancellationToken cancellationToken)
    {
        // Skip if already completed
        if (chunk.Status == ChunkStatus.Completed)
        {
            Console.WriteLine($"[DownloadEngine] Chunk {chunk.Index} already completed, skipping");
            chunk.DownloadedBytes = chunk.TotalBytes;
            return;
        }

        Console.WriteLine($"[DownloadEngine] Starting chunk {chunk.Index} ({chunk.StartByte}-{chunk.EndByte})");
        chunk.Status = ChunkStatus.Downloading;

        var progress = new Progress<long>(bytesRead =>
        {
            chunk.DownloadedBytes = bytesRead;
            UpdateProgress(task);
        });

        try
        {
            await _httpService.DownloadChunkAsync(
                task.Url,
                chunk.StartByte,
                chunk.EndByte,
                chunk.TempFilePath,
                progress,
                cancellationToken
            );

            chunk.Status = ChunkStatus.Completed;
            Console.WriteLine($"[DownloadEngine] Chunk {chunk.Index} completed");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[DownloadEngine] Chunk {chunk.Index} paused");
            // Keep current downloaded bytes for resume
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DownloadEngine] ERROR in chunk {chunk.Index}: {ex.GetType().Name}");
            Console.WriteLine($"[DownloadEngine] ERROR Message: {ex.Message}");
            chunk.Status = ChunkStatus.Failed;
            throw;
        }
    }

    private void UpdateProgress(DownloadTask task)
    {
        var totalDownloaded = task.Chunks.Sum(c => c.DownloadedBytes);
        task.DownloadedBytes = totalDownloaded;

        // Throttle updates to avoid too frequent changes
        if (!_lastUpdateTime.TryGetValue(task.Id, out var lastUpdate))
            return;

        var timeSinceLastUpdate = (DateTime.UtcNow - lastUpdate).TotalSeconds;
        if (timeSinceLastUpdate < UPDATE_INTERVAL_SECONDS)
            return;

        _lastUpdateTime[task.Id] = DateTime.UtcNow;

        // Calculate instantaneous speed
        if (!_speedWatches.TryGetValue(task.Id, out var watch) || 
            !_lastDownloadedBytes.TryGetValue(task.Id, out var lastBytes))
            return;

        var elapsedSeconds = watch.Elapsed.TotalSeconds;
        var instantSpeed = elapsedSeconds > 0 ? (totalDownloaded - lastBytes) / elapsedSeconds : 0;
        _lastDownloadedBytes[task.Id] = totalDownloaded;
        watch.Restart();

        // Apply moving average to smooth speed
        if (!_speedHistory.TryGetValue(task.Id, out var speedQueue))
            return;

        speedQueue.Enqueue(instantSpeed);
        if (speedQueue.Count > SPEED_HISTORY_SIZE)
            speedQueue.Dequeue();

        var smoothedSpeed = speedQueue.Average();

        // Calculate ETA using smoothed speed
        var remainingBytes = task.TotalBytes - totalDownloaded;
        var eta = smoothedSpeed > 0 ? TimeSpan.FromSeconds(remainingBytes / smoothedSpeed) : TimeSpan.Zero;

        var progress = new DownloadProgress
        {
            TaskId = task.Id,
            DownloadedBytes = totalDownloaded,
            TotalBytes = task.TotalBytes,
            Speed = smoothedSpeed,
            EstimatedTimeRemaining = eta,
            ProgressPercentage = task.Progress,
            ActiveConnections = task.Chunks.Count(c => c.Status == ChunkStatus.Downloading)
        };

        ProgressChanged?.Invoke(this, progress);
    }
}
