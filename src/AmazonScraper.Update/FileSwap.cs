namespace AmazonScraper.Update;

public sealed class StagedFile
{
    public required string RelativePath { get; init; }

    public required string SourcePath { get; init; }

    public required string Mode { get; init; }

    public required long Size { get; init; }

    public required string Sha256 { get; init; }
}

public sealed class SwapEntry
{
    public required string RelativePath { get; init; }

    public required string TargetPath { get; init; }

    public bool Existed { get; init; }

    public string? OldPath { get; init; }
}

public sealed class SwapResult
{
    public required string InstallDir { get; init; }

    public required string RollbackDir { get; init; }

    public required List<SwapEntry> Entries { get; init; }

    public bool Committed { get; set; }

    public bool RolledBack { get; set; }
}

/// <summary>
/// Replaces files while keeping the old copies next to the installation.
/// Rollback is a rename back, not an archive extraction, so it is fast and
/// works even when the backup archive is on another filesystem.
/// </summary>
public static class FileSwap
{
    public static SwapResult Apply(
        string installDir,
        IReadOnlyList<StagedFile> files,
        string rollbackDir,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(installDir);
        Directory.CreateDirectory(rollbackDir);
        UpdatePaths.MarkHidden(rollbackDir);
        var result = new SwapResult
        {
            InstallDir = installDir,
            RollbackDir = rollbackDir,
            Entries = [],
        };

        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = PathSafety.CombineSafe(installDir, file.RelativePath);
                var targetDirectory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                string? oldPath = null;
                var existed = File.Exists(target);
                if (existed)
                {
                    oldPath = PathSafety.CombineSafe(rollbackDir, file.RelativePath);
                    var oldDirectory = Path.GetDirectoryName(oldPath);
                    if (!string.IsNullOrEmpty(oldDirectory))
                    {
                        Directory.CreateDirectory(oldDirectory);
                    }
                    File.Move(target, oldPath, overwrite: true);
                }

                MoveOrCopy(file.SourcePath, target);
                TrySetMode(target, file.Mode);

                result.Entries.Add(new SwapEntry
                {
                    RelativePath = file.RelativePath,
                    TargetPath = target,
                    Existed = existed,
                    OldPath = oldPath,
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            TryRollback(result);
            throw new UpdateException(
                $"Sostituzione dei file non riuscita: {ex.Message}",
                ex);
        }
    }

    public static bool Rollback(SwapResult result)
    {
        if (result.Committed)
        {
            throw new UpdateException("Cannot roll back an update that has already completed.");
        }
        if (result.RolledBack)
        {
            return true;
        }

        return TryRollback(result);
    }

    public static void Commit(SwapResult result)
    {
        result.Committed = true;
        TryDeleteDirectory(result.RollbackDir);
    }

    private static bool TryRollback(SwapResult result)
    {
        if (result.RolledBack)
        {
            return true;
        }

        var restoredAll = true;
        for (var index = result.Entries.Count - 1; index >= 0; index--)
        {
            var entry = result.Entries[index];
            try
            {
                if (File.Exists(entry.TargetPath))
                {
                    File.Delete(entry.TargetPath);
                }
                if (entry.Existed && entry.OldPath is not null && File.Exists(entry.OldPath))
                {
                    var directory = Path.GetDirectoryName(entry.TargetPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.Move(entry.OldPath, entry.TargetPath, overwrite: true);
                }
            }
            catch (Exception)
            {
                restoredAll = false;
            }
        }

        if (restoredAll)
        {
            result.RolledBack = true;
            TryDeleteDirectory(result.RollbackDir);
        }
        return restoredAll;
    }

    private static void MoveOrCopy(string source, string target)
    {
        try
        {
            File.Move(source, target, overwrite: true);
        }
        catch (IOException)
        {
            File.Copy(source, target, overwrite: true);
            File.Delete(source);
        }
    }

    private static void TrySetMode(string path, string mode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var unixMode = mode == "0755"
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite
                    | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
            File.SetUnixFileMode(path, unixMode);
        }
        catch (Exception)
        {
            // On filesystems without Unix modes the executable keeps its
            // inherited mode; the verifier will catch a real problem.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
