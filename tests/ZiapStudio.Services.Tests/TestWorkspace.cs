namespace ZiapStudio.Services.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "ziap-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string CreateDirectory(params string[] parts)
    {
        var path = parts.Aggregate(RootPath, Path.Combine);
        Directory.CreateDirectory(path);
        return Path.GetFullPath(path);
    }

    public string WriteFile(string relativePath, string contents = "")
    {
        var path = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return Path.GetFullPath(path);
    }

    public string WriteBytes(string relativePath, byte[] contents)
    {
        var path = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);
        return Path.GetFullPath(path);
    }

    public string WritePngHeader(string relativePath, int width, int height)
    {
        var header = new byte[24];
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        signature.CopyTo(header, 0);
        header[12] = (byte)'I';
        header[13] = (byte)'H';
        header[14] = (byte)'D';
        header[15] = (byte)'R';
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20, 4), height);
        return WriteBytes(relativePath, header);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
