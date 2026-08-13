namespace ZiapStudio.Services;

public sealed class FileSystemService
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern) =>
        Directory.EnumerateFiles(path, searchPattern, SearchOption.TopDirectoryOnly);

    public IEnumerable<string> EnumerateFilesRecursively(string path, string searchPattern) =>
        Directory.EnumerateFiles(path, searchPattern, SearchOption.AllDirectories);

    public IEnumerable<string> EnumerateDirectories(string path) =>
        Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly);

    public string GetFullPath(string path) => Path.GetFullPath(path);

    public string GetDirectoryName(string path)
    {
        var trimmedPath = Path.TrimEndingDirectorySeparator(path);
        var directoryName = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(directoryName) ? trimmedPath : directoryName;
    }

    public Task<string> ReadAllTextAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(path, cancellationToken);

    public async Task<byte[]> ReadBytesAsync(
        string path,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);

        await using var stream = File.OpenRead(path);
        var buffer = new byte[Math.Min(maximumCount, checked((int)Math.Min(stream.Length, int.MaxValue)))];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            offset += bytesRead;
        }

        return offset == buffer.Length ? buffer : buffer[..offset];
    }

    public Task<byte[]> ReadAllBytesAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        File.ReadAllBytesAsync(path, cancellationToken);

    public DateTimeOffset GetLastWriteTimeUtc(string path) =>
        new(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);

    public long GetFileLength(string path) => new FileInfo(path).Length;

    public async Task WriteAllBytesWithFlushAsync(
        string path,
        ReadOnlyMemory<byte> contents,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(contents, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    public Task WriteAllTextAsync(
        string path,
        string contents,
        CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, contents, cancellationToken);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Move(sourcePath, destinationPath, overwrite);

    public void DeleteFile(string path) => File.Delete(path);
}
