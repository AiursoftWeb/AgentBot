using System.Text;
using Microsoft.Extensions.Logging;

namespace Aiursoft.AgentBot.Services;

public sealed record PlanningRepositorySnapshot(
    IReadOnlyList<string> Files,
    IReadOnlyDictionary<string, string> TextFiles,
    int TotalBytes);

public sealed class PlanningRepositoryReader(ILogger<PlanningRepositoryReader> logger)
{
    internal const int MaxFiles = 500;
    internal const int MaxTextFiles = 200;
    internal const int MaxFileBytes = 128 * 1024;
    internal const int MaxTotalBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env", "auth.json", "credentials", "credentials.json", "id_rsa", "id_ed25519"
    };

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".next", ".nuxt", ".venv", "bin", "coverage", "dist", "node_modules", "obj",
        "out", "packages", "target", "vendor", "venv"
    };

    public async Task<PlanningRepositorySnapshot> ReadAsync(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException($"Planning repository preflight failed [repository_missing]: '{root}' does not exist.");
        }

        try
        {
            var files = new List<string>();
            var textFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var totalBytes = 0;

            foreach (var candidate in EnumerateSafeFiles(root))
            {
                var relativePath = Path.GetRelativePath(root, candidate).Replace(Path.DirectorySeparatorChar, '/');
                if (IsSensitive(relativePath))
                {
                    continue;
                }

                var safePath = ResolveSafeFile(root, relativePath);
                var info = new FileInfo(safePath);
                if (info.Length > MaxFileBytes)
                {
                    continue;
                }

                var bytes = await File.ReadAllBytesAsync(safePath);
                if (LooksBinary(bytes))
                {
                    continue;
                }

                if (files.Count >= MaxFiles)
                {
                    break;
                }

                files.Add(relativePath);
                if (textFiles.Count >= MaxTextFiles || totalBytes + bytes.Length > MaxTotalBytes)
                {
                    continue;
                }

                textFiles[relativePath] = Encoding.UTF8.GetString(bytes);
                totalBytes += bytes.Length;
            }

            if (files.Count == 0 || textFiles.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Planning repository preflight failed [no_readable_text]: '{root}' contains no readable text files.");
            }

            logger.LogInformation(
                "Planning repository preflight succeeded. IsolationBackend={IsolationBackend}, MountMode={MountMode}, Files={FileCount}, TextFiles={TextFileCount}, Bytes={ByteCount}",
                "bounded-snapshot-reader", "application-read-only", files.Count, textFiles.Count, totalBytes);
            return new PlanningRepositorySnapshot(files, textFiles, totalBytes);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"Planning repository preflight failed [permission_denied]: '{root}' is not readable.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Planning repository preflight failed [read_error]: '{root}' could not be read.", ex);
        }
    }

    private static IEnumerable<string> EnumerateSafeFiles(string root)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).OrderBy(path => path, StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    var name = Path.GetFileName(entry);
                    if (!IgnoredDirectoryNames.Contains(name))
                    {
                        pending.Enqueue(entry);
                    }
                }
                else
                {
                    yield return entry;
                }
            }
        }
    }

    internal static string ResolveSafeFile(string repositoryRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Repository paths must be non-empty relative paths.");
        }

        var root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Repository path escapes the planning root: '{relativePath}'.");
        }

        var current = root.TrimEnd(Path.DirectorySeparatorChar);
        foreach (var part in Path.GetRelativePath(root, fullPath).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Symbolic links are not readable in planning mode: '{relativePath}'.");
            }
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Repository file does not exist.", relativePath);
        }

        return fullPath;
    }

    private static bool IsSensitive(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        return SensitiveNames.Contains(name) || name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".key", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        var sample = bytes[..Math.Min(bytes.Length, 8192)];
        return sample.Contains((byte)0);
    }
}
