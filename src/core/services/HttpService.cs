using System.Net;

namespace XastDownloader.Core.Services;

public class HttpService
{
    private readonly HttpClient _httpClient;

    public HttpService()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 128
        };
        
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
    }

    public async Task<(bool supportsRanges, long fileSize, string? fileName)> GetFileInfoAsync(string url)
    {
        Console.WriteLine($"[HttpService] GetFileInfoAsync called for URL: {url}");
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            Console.WriteLine($"[HttpService] Sending HEAD request...");
            var response = await _httpClient.SendAsync(request);
            Console.WriteLine($"[HttpService] Response status: {response.StatusCode}");
            response.EnsureSuccessStatusCode();

            var supportsRanges = response.Headers.AcceptRanges?.Contains("bytes") ?? false;
            var fileSize = response.Content.Headers.ContentLength ?? 0;
            
            var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') 
                          ?? Path.GetFileName(new Uri(url).LocalPath);

            Console.WriteLine($"[HttpService] GetFileInfoAsync completed - SupportsRanges: {supportsRanges}, Size: {fileSize}, Name: {fileName}");
            return (supportsRanges, fileSize, fileName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HttpService] ERROR in GetFileInfoAsync: {ex.GetType().Name}");
            Console.WriteLine($"[HttpService] ERROR Message: {ex.Message}");
            Console.WriteLine($"[HttpService] ERROR StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    public async Task DownloadChunkAsync(
        string url, 
        long startByte, 
        long endByte, 
        string tempFilePath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[HttpService] DownloadChunkAsync called - Range: {startByte}-{endByte}, TempFile: {tempFilePath}");
        try
        {
            // Check if temp file exists and resume from where we left off
            long alreadyDownloaded = 0;
            FileMode fileMode = FileMode.Create;
            
            if (File.Exists(tempFilePath))
            {
                var fileInfo = new FileInfo(tempFilePath);
                alreadyDownloaded = fileInfo.Length;
                
                if (alreadyDownloaded > 0)
                {
                    Console.WriteLine($"[HttpService] Resuming from byte {alreadyDownloaded}");
                    startByte += alreadyDownloaded;
                    fileMode = FileMode.Append;
                    progress?.Report(alreadyDownloaded);
                }
            }

            // If already fully downloaded, skip
            if (startByte > endByte)
            {
                Console.WriteLine($"[HttpService] Chunk already fully downloaded");
                progress?.Report(endByte - (startByte - alreadyDownloaded) + 1);
                return;
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(startByte, endByte);

            Console.WriteLine($"[HttpService] Sending GET request with range...");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            Console.WriteLine($"[HttpService] Response status: {response.StatusCode}");
            response.EnsureSuccessStatusCode();

            Console.WriteLine($"[HttpService] Opening streams...");
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(tempFilePath, fileMode, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = alreadyDownloaded;
            int bytesRead;

            Console.WriteLine($"[HttpService] Starting data transfer...");
            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;
                progress?.Report(totalRead);
            }
            Console.WriteLine($"[HttpService] Chunk download completed - Total bytes: {totalRead}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HttpService] ERROR in DownloadChunkAsync: {ex.GetType().Name}");
            Console.WriteLine($"[HttpService] ERROR Message: {ex.Message}");
            Console.WriteLine($"[HttpService] ERROR StackTrace: {ex.StackTrace}");
            throw;
        }
    }
}
