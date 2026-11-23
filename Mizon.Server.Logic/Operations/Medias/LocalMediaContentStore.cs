using Mizon.Server.Logic.Responses.Medias;

namespace Mizon.Server.Logic.Operations.Medias;

/// <summary>Opens database-registered local assets inside deployment-owned media directories.</summary>
public sealed class LocalMediaContentStore(IEnumerable<string> allowedRoots)
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly string[] roots = allowedRoots.Select(NormalizeLocalPath).OfType<string>()
        .Select(root => Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar)
        .ToArray();

    public (FileStream Stream, string ContentType)? TryOpen(MediaResponse media)
    {
        if (media.FileSize <= 0 || string.IsNullOrEmpty(media.StoragePath)
            || !media.StoragePath.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(media.StoragePath, UriKind.Absolute, out var uri)
            || !uri.IsFile || uri.IsUnc || uri.Host.Length != 0 || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || NormalizeLocalPath(uri.LocalPath) is not { } path
            || !roots.Any(root => path.StartsWith(root, PathComparison))
            || ContentTypeFor(path) is not { } contentType)
            return null;

        try
        {
            var fileAttributes = File.GetAttributes(path);
            if ((fileAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                return null;

            // Checking every ancestor also covers a reparse point replacing the configured root.
            for (var directory = Directory.GetParent(path); directory is not null; directory = directory.Parent)
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    return null;
            }

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
            try
            {
                if (stream.Length == media.FileSize)
                    return (stream, contentType);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
            stream.Dispose();
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? NormalizeLocalPath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            return null;
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full)!;
            if (root.Length == full.Length || (OperatingSystem.IsWindows()
                && (root.StartsWith("\\\\", StringComparison.Ordinal) || full.AsSpan(root.Length).Contains(':'))))
                return null;
            return full;
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".mp4" or ".m4v" => "video/mp4",
        ".mov" => "video/quicktime",
        ".glb" => "model/gltf-binary",
        ".json" => "application/json",
        ".tgs" or ".lottie" => "application/octet-stream",
        _ => null
    };
}
