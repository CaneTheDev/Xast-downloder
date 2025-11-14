using XastDownloader.Core.Models;

namespace XastDownloader.Core.Engine;

public class ChunkManager
{
    private const long MinChunkSize = 1024 * 1024; // 1MB minimum per chunk

    public static List<Chunk> CreateChunks(long fileSize, int connectionCount)
    {
        Console.WriteLine($"[ChunkManager] CreateChunks called - FileSize: {fileSize}, ConnectionCount: {connectionCount}");
        
        // If file is too small, reduce connection count
        var maxConnections = (int)(fileSize / MinChunkSize);
        var actualConnections = Math.Min(connectionCount, Math.Max(1, maxConnections));
        Console.WriteLine($"[ChunkManager] Actual connections: {actualConnections}");

        var chunks = new List<Chunk>();
        var chunkSize = fileSize / actualConnections;
        Console.WriteLine($"[ChunkManager] Chunk size: {chunkSize} bytes");

        for (int i = 0; i < actualConnections; i++)
        {
            var startByte = i * chunkSize;
            var endByte = (i == actualConnections - 1) ? fileSize - 1 : (startByte + chunkSize - 1);

            chunks.Add(new Chunk
            {
                Index = i,
                StartByte = startByte,
                EndByte = endByte,
                Status = ChunkStatus.Pending
            });
            Console.WriteLine($"[ChunkManager] Created chunk {i}: {startByte}-{endByte}");
        }

        Console.WriteLine($"[ChunkManager] Total chunks created: {chunks.Count}");
        return chunks;
    }

    public static async Task MergeChunksAsync(List<Chunk> chunks, string outputPath)
    {
        Console.WriteLine($"[ChunkManager] MergeChunksAsync called - Output: {outputPath}");
        Console.WriteLine($"[ChunkManager] Total chunks to merge: {chunks.Count}");
        
        // Sort chunks by index to ensure correct order
        var sortedChunks = chunks.OrderBy(c => c.Index).ToList();

        try
        {
            Console.WriteLine($"[ChunkManager] Creating output file stream...");
            await using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

            foreach (var chunk in sortedChunks)
            {
                Console.WriteLine($"[ChunkManager] Merging chunk {chunk.Index} from {chunk.TempFilePath}");
                if (!File.Exists(chunk.TempFilePath))
                {
                    Console.WriteLine($"[ChunkManager] ERROR: Chunk file not found: {chunk.TempFilePath}");
                    throw new FileNotFoundException($"Chunk file not found: {chunk.TempFilePath}");
                }

                await using var chunkStream = new FileStream(chunk.TempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await chunkStream.CopyToAsync(outputStream);
                Console.WriteLine($"[ChunkManager] Chunk {chunk.Index} merged successfully");
            }
            Console.WriteLine($"[ChunkManager] All chunks merged successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ChunkManager] ERROR in MergeChunksAsync: {ex.GetType().Name}");
            Console.WriteLine($"[ChunkManager] ERROR Message: {ex.Message}");
            Console.WriteLine($"[ChunkManager] ERROR StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    public static void CleanupTempFiles(List<Chunk> chunks)
    {
        foreach (var chunk in chunks)
        {
            if (File.Exists(chunk.TempFilePath))
            {
                try
                {
                    File.Delete(chunk.TempFilePath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}
