using System.Security.Cryptography;
using System.Text;

namespace ZiapStudio.Services.Integration.Remote;

public static class LocalizationChecksum
{
    public const string Sha256 = "sha256";
    public const string LegacyFnv1A32 = "fnv1a32";

    public static string ResolveAlgorithm(string? declaredAlgorithm, string? checksum)
    {
        var normalized = declaredAlgorithm?.Trim().ToLowerInvariant();
        if (normalized is "sha-256" or Sha256)
        {
            return Sha256;
        }

        if (normalized is "fnv-1a-32" or "fnv1a-32" or LegacyFnv1A32)
        {
            return LegacyFnv1A32;
        }

        var normalizedChecksum = Normalize(checksum);
        return normalizedChecksum?.Length switch
        {
            64 => Sha256,
            8 => LegacyFnv1A32,
            _ => Sha256,
        };
    }

    public static string Compute(ReadOnlySpan<byte> contents, string algorithm) =>
        ResolveAlgorithm(algorithm, null) switch
        {
            LegacyFnv1A32 => ComputeLegacyFnv1A32(contents),
            _ => Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant(),
        };

    public static string? Normalize(string? checksum)
    {
        var normalized = checksum?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var separator = normalized.IndexOf(':');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    private static string ComputeLegacyFnv1A32(ReadOnlySpan<byte> contents)
    {
        var text = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetString(contents);
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var hash = 2166136261u;
        foreach (var character in text)
        {
            hash ^= character;
            hash = unchecked(hash * 16777619u);
        }

        return hash.ToString("x8");
    }
}
