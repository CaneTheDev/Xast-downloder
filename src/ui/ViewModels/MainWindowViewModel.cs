using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XastDownloader.Core.Engine;
using XastDownloader.Core.Models;
using XastDownloader.Core.Utils;

namespace ui.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DownloadEngine _downloadEngine;
    private readonly Dictionary<string, DownloadItemViewModel> _activeDownloadItems = new();
    private readonly Dictionary<string, Task> _downloadTasks = new();

    [ObservableProperty]
    private string _downloadUrl = "";

    [ObservableProperty]
    private string _savePath = "";

    [ObservableProperty]
    private string _saveDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [ObservableProperty]
    private int _connectionCount = 32;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private double _progress = 0;

    [ObservableProperty]
    private string _speedText = "";

    [ObservableProperty]
    private string _downloadedText = "";

    [ObservableProperty]
    private string _etaText = "";

    [ObservableProperty]
    private bool _isDownloading = false;

    [ObservableProperty]
    private bool _isTesting = false;

    [ObservableProperty]
    private string _serverCapabilities = "";

    public ObservableCollection<int> ConnectionOptions { get; } = new() { 16, 32, 64, 128 };
    
    public ObservableCollection<DownloadItemViewModel> ActiveDownloads { get; } = new();
    
    public ObservableCollection<DownloadItemViewModel> DownloadHistory { get; } = new();

    public MainWindowViewModel()
    {
        _downloadEngine = new DownloadEngine();
        _downloadEngine.ProgressChanged += OnProgressChanged;
    }

    [RelayCommand]
    private async Task StartDownload()
    {
        Console.WriteLine($"[MainWindowViewModel] StartDownload called");
        Console.WriteLine($"[MainWindowViewModel] URL: {DownloadUrl}");
        Console.WriteLine($"[MainWindowViewModel] SaveDirectory: {SaveDirectory}");
        Console.WriteLine($"[MainWindowViewModel] ConnectionCount: {ConnectionCount}");
        
        if (string.IsNullOrWhiteSpace(DownloadUrl))
        {
            Console.WriteLine($"[MainWindowViewModel] ERROR: URL is empty");
            StatusMessage = "Please enter a URL";
            return;
        }

        StatusMessage = "Fetching file info...";

        try
        {
            Console.WriteLine($"[MainWindowViewModel] Creating HttpService...");
            // Get file info first to get the filename
            var httpService = new XastDownloader.Core.Services.HttpService();
            Console.WriteLine($"[MainWindowViewModel] Calling GetFileInfoAsync...");
            var (supportsRanges, fileSize, fileName) = await httpService.GetFileInfoAsync(DownloadUrl);
            Console.WriteLine($"[MainWindowViewModel] File info received - SupportsRanges: {supportsRanges}, Size: {fileSize}, Name: {fileName}");

            // Build full save path using current SaveDirectory
            var fullPath = Path.Combine(SaveDirectory, fileName ?? "download");
            Console.WriteLine($"[MainWindowViewModel] Initial save path: {fullPath}");
            
            // Handle duplicate filenames (add (1), (2), etc.)
            fullPath = GetUniqueFilePath(fullPath);
            Console.WriteLine($"[MainWindowViewModel] Unique save path: {fullPath}");

            // Ensure directory exists
            var directory = Path.GetDirectoryName(fullPath);
            Console.WriteLine($"[MainWindowViewModel] Directory: {directory}");
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Console.WriteLine($"[MainWindowViewModel] Creating directory: {directory}");
                Directory.CreateDirectory(directory);
            }

            SavePath = fullPath;
            
            // Capture values before clearing them for the background task
            var capturedUrl = DownloadUrl;
            var capturedConnectionCount = ConnectionCount;
            
            // Create download item and add to active list
            Console.WriteLine($"[MainWindowViewModel] Creating download item...");
            var downloadItem = new DownloadItemViewModel
            {
                FileName = Path.GetFileName(fullPath),
                Url = capturedUrl,
                Status = supportsRanges ? "Starting..." : "Starting (server doesn't support parallel downloads)...",
                Connections = supportsRanges ? capturedConnectionCount : 1,
                RequestedConnections = capturedConnectionCount,
                ServerSupportsRanges = supportsRanges,
                DownloadStatus = DownloadStatus.Downloading,
                OnPause = PauseDownload,
                OnResume = ResumeDownload,
                OnCancel = CancelDownload
            };
            
            Console.WriteLine($"[MainWindowViewModel] Adding to ActiveDownloads...");
            ActiveDownloads.Add(downloadItem);
            StatusMessage = "Starting download...";

            // Start download in background
            Console.WriteLine($"[MainWindowViewModel] Starting background download task...");
            var downloadTask = Task.Run(async () =>
            {
                try
                {
                    Console.WriteLine($"[MainWindowViewModel] Inside Task.Run - calling StartDownloadAsync...");
                    var task = await _downloadEngine.StartDownloadAsync(
                        capturedUrl,
                        fullPath,
                        capturedConnectionCount
                    );
                    Console.WriteLine($"[MainWindowViewModel] StartDownloadAsync completed with status: {task.Status}");

                    // Update item status on UI thread
                    Console.WriteLine($"[MainWindowViewModel] Updating download item status on UI thread...");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        // Only update if the item still exists and hasn't been manually changed
                        if (_activeDownloadItems.ContainsKey(downloadItem.TaskId))
                        {
                            downloadItem.DownloadStatus = task.Status;
                            downloadItem.Status = task.Status.ToString();
                            
                            if (task.Status == DownloadStatus.Completed)
                            {
                                downloadItem.Progress = 100;
                                
                                // Move completed downloads to history
                                ActiveDownloads.Remove(downloadItem);
                                DownloadHistory.Insert(0, downloadItem);
                                _activeDownloadItems.Remove(downloadItem.TaskId);
                                _downloadTasks.Remove(downloadItem.TaskId);
                                
                                StatusMessage = $"Download completed! Saved to: {fullPath}";
                            }
                            else if (task.Status == DownloadStatus.Paused)
                            {
                                // Keep paused downloads in active list
                                downloadItem.Status = "Paused";
                                StatusMessage = "Download paused";
                            }
                            else
                            {
                                // Move failed/cancelled to history
                                ActiveDownloads.Remove(downloadItem);
                                DownloadHistory.Insert(0, downloadItem);
                                _activeDownloadItems.Remove(downloadItem.TaskId);
                                _downloadTasks.Remove(downloadItem.TaskId);
                                
                                StatusMessage = $"Download {task.Status}";
                            }
                        }
                    });
                    
                    Console.WriteLine($"[MainWindowViewModel] Download task finished successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MainWindowViewModel] ERROR in download task: {ex.GetType().Name}");
                    Console.WriteLine($"[MainWindowViewModel] ERROR Message: {ex.Message}");
                    Console.WriteLine($"[MainWindowViewModel] ERROR StackTrace: {ex.StackTrace}");
                    
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        downloadItem.Status = "Failed";
                        downloadItem.DownloadStatus = DownloadStatus.Failed;
                        
                        ActiveDownloads.Remove(downloadItem);
                        DownloadHistory.Insert(0, downloadItem);
                        _activeDownloadItems.Remove(downloadItem.TaskId);
                        _downloadTasks.Remove(downloadItem.TaskId);
                        
                        StatusMessage = $"Error: {ex.Message}";
                    });
                }
            });

            // Store task ID for tracking
            downloadItem.TaskId = Guid.NewGuid().ToString();
            Console.WriteLine($"[MainWindowViewModel] Task ID: {downloadItem.TaskId}");
            _activeDownloadItems[downloadItem.TaskId] = downloadItem;
            _downloadTasks[downloadItem.TaskId] = downloadTask;

            // Clear URL for next download
            DownloadUrl = "";
            Console.WriteLine($"[MainWindowViewModel] StartDownload method completed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainWindowViewModel] ERROR in StartDownload: {ex.GetType().Name}");
            Console.WriteLine($"[MainWindowViewModel] ERROR Message: {ex.Message}");
            Console.WriteLine($"[MainWindowViewModel] ERROR StackTrace: {ex.StackTrace}");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private void PauseDownload(DownloadItemViewModel item)
    {
        Console.WriteLine($"[MainWindowViewModel] Pausing download: {item.TaskId}");
        
        // Update UI immediately to prevent button flickering
        item.DownloadStatus = DownloadStatus.Paused;
        item.Status = "Pausing...";
        
        // Then trigger the actual pause
        _downloadEngine.PauseDownload(item.TaskId);
    }

    private void ResumeDownload(DownloadItemViewModel item)
    {
        Console.WriteLine($"[MainWindowViewModel] Resuming download: {item.TaskId}");
        
        // Update UI immediately
        item.Status = "Resuming...";
        item.DownloadStatus = DownloadStatus.Downloading;

        // Start resume in background
        var resumeTask = Task.Run(async () =>
        {
            try
            {
                Console.WriteLine($"[MainWindowViewModel] Calling ResumeDownloadAsync...");
                var task = await _downloadEngine.ResumeDownloadAsync(item.TaskId);
                Console.WriteLine($"[MainWindowViewModel] Resume completed with status: {task.Status}");

                // Update item status on UI thread
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    item.DownloadStatus = task.Status;
                    item.Status = task.Status.ToString();
                    
                    if (task.Status == DownloadStatus.Completed)
                    {
                        item.Progress = 100;
                        
                        // Move to history
                        ActiveDownloads.Remove(item);
                        DownloadHistory.Insert(0, item);
                        _activeDownloadItems.Remove(item.TaskId);
                        _downloadTasks.Remove(item.TaskId);
                        
                        StatusMessage = $"Download completed! Saved to: {task.SavePath}";
                    }
                    else if (task.Status == DownloadStatus.Paused)
                    {
                        StatusMessage = "Download paused";
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainWindowViewModel] ERROR in resume: {ex.GetType().Name}");
                Console.WriteLine($"[MainWindowViewModel] ERROR Message: {ex.Message}");
                
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    item.Status = $"Resume failed: {ex.Message}";
                    item.DownloadStatus = DownloadStatus.Failed;
                    StatusMessage = $"Resume error: {ex.Message}";
                });
            }
        });

        _downloadTasks[item.TaskId] = resumeTask;
    }

    private void CancelDownload(DownloadItemViewModel item)
    {
        Console.WriteLine($"[MainWindowViewModel] Cancelling download: {item.TaskId}");
        
        // Update UI immediately
        item.DownloadStatus = DownloadStatus.Cancelled;
        item.Status = "Cancelling...";
        
        // Trigger cancellation
        _downloadEngine.CancelDownload(item.TaskId);
        
        // Move to history
        ActiveDownloads.Remove(item);
        DownloadHistory.Insert(0, item);
        _activeDownloadItems.Remove(item.TaskId);
        _downloadTasks.Remove(item.TaskId);
        
        StatusMessage = "Download cancelled";
    }

    private string GetUniqueFilePath(string filePath)
    {
        if (!File.Exists(filePath))
            return filePath;

        var directory = Path.GetDirectoryName(filePath) ?? "";
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);
        var counter = 1;

        string newPath;
        do
        {
            newPath = Path.Combine(directory, $"{fileNameWithoutExt} ({counter}){extension}");
            counter++;
        } while (File.Exists(newPath));

        return newPath;
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        Console.WriteLine($"[MainWindowViewModel] TestConnection called");
        
        if (string.IsNullOrWhiteSpace(DownloadUrl))
        {
            StatusMessage = "Please enter a URL to test";
            return;
        }

        IsTesting = true;
        StatusMessage = "Testing connection...";
        ServerCapabilities = "";

        try
        {
            var httpService = new XastDownloader.Core.Services.HttpService();
            var (supportsRanges, fileSize, fileName) = await httpService.GetFileInfoAsync(DownloadUrl);

            Console.WriteLine($"[MainWindowViewModel] Test results - SupportsRanges: {supportsRanges}, Size: {fileSize}");

            if (!supportsRanges)
            {
                ServerCapabilities = "⚠️ Server doesn't support parallel downloads (no range support)\nRecommended: 1 connection";
                StatusMessage = "Server only supports single connection";
            }
            else
            {
                // Calculate recommended connections based on file size
                // Using 5MB minimum chunk size for better efficiency
                var minChunkSize = 5 * 1024 * 1024; // 5MB per chunk
                var maxPossibleConnections = (int)(fileSize / minChunkSize);
                
                int recommended;
                string reasoning;
                
                if (fileSize < 50 * 1024 * 1024) // Less than 50MB
                {
                    recommended = Math.Min(16, Math.Max(1, maxPossibleConnections));
                    reasoning = "Small file - fewer connections reduce overhead";
                }
                else if (fileSize < 200 * 1024 * 1024) // 50-200MB
                {
                    recommended = 32;
                    reasoning = "Medium file - 32 connections is optimal";
                }
                else if (fileSize < 500 * 1024 * 1024) // 200-500MB
                {
                    recommended = 64;
                    reasoning = "Large file - 64 connections recommended";
                }
                else // 500MB+
                {
                    recommended = 64; // Still recommend 64, not 128
                    reasoning = "Very large file - 64 connections (128 often slower due to overhead)";
                }

                ServerCapabilities = $"✅ Server supports parallel downloads\n" +
                                   $"File size: {FormatHelper.FormatBytes(fileSize)}\n" +
                                   $"Recommended: {recommended} connections\n" +
                                   $"💡 {reasoning}";

                // Auto-set the connection count to recommended value
                if (ConnectionOptions.Contains(recommended))
                {
                    ConnectionCount = recommended;
                }

                StatusMessage = $"Test complete - Recommended: {recommended} connections";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainWindowViewModel] ERROR in TestConnection: {ex.Message}");
            ServerCapabilities = $"❌ Connection test failed: {ex.Message}";
            StatusMessage = "Connection test failed";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task BrowseSavePath()
    {
        try
        {
            var dialog = new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "Select Download Folder",
                AllowMultiple = false
            };

            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (mainWindow?.StorageProvider is { } storageProvider)
            {
                var result = await storageProvider.OpenFolderPickerAsync(dialog);
                
                if (result.Count > 0)
                {
                    SaveDirectory = result[0].Path.LocalPath;
                    StatusMessage = $"Save folder: {SaveDirectory}";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error selecting folder: {ex.Message}";
        }
    }

    private void OnProgressChanged(object? sender, DownloadProgress progress)
    {
        // Find the download item by task ID
        var downloadItem = _activeDownloadItems.Values.FirstOrDefault();
        
        if (downloadItem != null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                downloadItem.Progress = progress.ProgressPercentage;
                downloadItem.Speed = FormatHelper.FormatSpeed(progress.Speed);
                downloadItem.Downloaded = $"{FormatHelper.FormatBytes(progress.DownloadedBytes)} / {FormatHelper.FormatBytes(progress.TotalBytes)}";
                downloadItem.Eta = FormatHelper.FormatTime(progress.EstimatedTimeRemaining);
                downloadItem.Status = downloadItem.ServerSupportsRanges ? "Downloading" : "Downloading (single connection)";
                downloadItem.Connections = progress.ActiveConnections;
            });
        }

        // Update main window progress for the first active download
        Progress = progress.ProgressPercentage;
        SpeedText = FormatHelper.FormatSpeed(progress.Speed);
        DownloadedText = $"{FormatHelper.FormatBytes(progress.DownloadedBytes)} / {FormatHelper.FormatBytes(progress.TotalBytes)}";
        EtaText = FormatHelper.FormatTime(progress.EstimatedTimeRemaining);
        StatusMessage = $"Downloading... ({progress.ActiveConnections} connections)";
    }
}
