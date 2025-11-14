// Quick test script - run with: dotnet script test-download.cs
using XastDownloader.Core.Engine;
using XastDownloader.Core.Utils;

var engine = new DownloadEngine();

engine.ProgressChanged += (sender, progress) =>
{
    Console.Write($"\r{progress.ProgressPercentage:F1}% | " +
                  $"{FormatHelper.FormatBytes(progress.DownloadedBytes)} / {FormatHelper.FormatBytes(progress.TotalBytes)} | " +
                  $"{FormatHelper.FormatSpeed(progress.Speed)} | " +
                  $"ETA: {FormatHelper.FormatTime(progress.EstimatedTimeRemaining)} | " +
                  $"Connections: {progress.ActiveConnections}");
};

Console.WriteLine("Starting download with 32 connections...");

var task = await engine.StartDownloadAsync(
    "https://speed.hetzner.de/100MB.bin",  // Test file
    "test-download.bin",
    connectionCount: 32
);

Console.WriteLine($"\n\nDownload {task.Status}!");
Console.WriteLine($"File: {task.FileName}");
Console.WriteLine($"Size: {FormatHelper.FormatBytes(task.TotalBytes)}");
Console.WriteLine($"Time: {(task.CompletedAt - task.StartedAt)?.TotalSeconds:F1}s");
