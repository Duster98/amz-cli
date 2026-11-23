namespace AmazonScraper.Update;

public static class UpdateInstaller
{
    public static async Task<VerifyResult> ApplyAsync(
        UpdateApplyPlan plan,
        UpdateJournal journal,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? journalPath = null)
    {
        Validate(plan);
        await ValidateStagedAsync(plan, cancellationToken).ConfigureAwait(false);

        journal.Phase = UpdatePhase.Applying;
        journal.InstallDir = plan.InstallDir;
        journal.FromVersion = plan.FromVersion;
        journal.ToVersion = plan.ToVersion;
        journal.Platform = plan.Platform;
        journal.Channel = plan.Channel;
        journal.BackupPath = plan.BackupPath;
        journal.BackupSha256 = plan.BackupSha256;
        journal.StagingDir = plan.StagingDir;
        journal.RollbackDir = plan.RollbackDir;
        journal.Error = null;
        UpdateJournalStore.Write(journal, journalPath);

        progress?.Report(new UpdateProgress(
            "apply",
            $"Applico {plan.Files.Count} file"));

        var staged = plan.Files.Select(file => new StagedFile
        {
            RelativePath = file.Path,
            SourcePath = file.SourcePath,
            Mode = file.Mode,
            Size = file.Size,
            Sha256 = file.Sha256,
        }).ToList();

        SwapResult? swap = null;
        try
        {
            swap = FileSwap.Apply(
                plan.InstallDir,
                staged,
                plan.RollbackDir,
                cancellationToken);

            journal.Phase = UpdatePhase.VerifyingInstall;
            UpdateJournalStore.Write(journal, journalPath);
            progress?.Report(new UpdateProgress("verify", "Verifying the new binary"));

            var verify = await UpdateVerifier.VerifyAsync(
                    InstallLocation.ExecutablePath(plan.InstallDir, plan.Platform),
                    plan.ToVersion,
                    plan.Platform,
                    plan.RequireCurl,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!verify.Success)
            {
                journal.Error = verify.Detail;
                throw new UpdateException(
                    $"The new binary failed its test; rollback in progress. {verify.Detail}");
            }

            InstallStateStore.Write(
                plan.InstallDir,
                plan.ToVersion,
                plan.Platform,
                plan.Manifest,
                plan.Signature);
            FileSwap.Commit(swap);
            InstallManifestUpdater.Update(
                plan.InstallDir,
                plan.ToVersion,
                plan.Platform,
                plan.Channel,
                plan.Files.Select(file => file.Path).ToList(),
                plan.ReleasePublishedAt);

            journal.Phase = UpdatePhase.Completed;
            journal.Error = null;
            UpdateJournalStore.Write(journal, journalPath);
            progress?.Report(new UpdateProgress(
                "completed",
                $"Update to {plan.ToVersion} completed"));
            TryDeleteDirectory(plan.StagingDir);
            return verify;
        }
        catch (UpdateException)
        {
            var rolledBack = true;
            if (swap is not null && !swap.Committed)
            {
                rolledBack = FileSwap.Rollback(swap);
            }
            journal.Phase = rolledBack
                ? UpdatePhase.RolledBack
                : UpdatePhase.Failed;
            journal.Error ??= rolledBack
                ? "Update failed."
                : $"Rollback failed. Backup: {plan.BackupPath}";
            UpdateJournalStore.Write(journal, journalPath);
            TryDeleteDirectory(plan.StagingDir);
            throw;
        }
        catch (Exception ex)
        {
            var rolledBack = true;
            if (swap is not null && !swap.Committed)
            {
                rolledBack = FileSwap.Rollback(swap);
            }
            journal.Phase = UpdatePhase.Failed;
            journal.Error = rolledBack
                ? ex.Message
                : $"{ex.Message} Rollback failed. Backup: {plan.BackupPath}";
            UpdateJournalStore.Write(journal, journalPath);
            TryDeleteDirectory(plan.StagingDir);
            throw new UpdateException(
                $"Update failed: {ex.Message}",
                ex);
        }
    }

    public static async Task ValidateStagedAsync(
        UpdateApplyPlan plan,
        CancellationToken cancellationToken = default)
    {
        foreach (var file in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(file.SourcePath)
                || new FileInfo(file.SourcePath).Length != file.Size)
            {
                throw new UpdateException(
                    $"Staged file is missing or has the wrong size: '{file.Path}'.");
            }
            var sourceHash = await FileHash
                .Sha256Async(file.SourcePath, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(sourceHash, file.Sha256, StringComparison.Ordinal))
            {
                throw new UpdateException(
                    $"Invalid staged file hash: '{file.Path}'.");
            }
        }
    }

    public static void Validate(UpdateApplyPlan plan)
    {
        if (plan.Schema != 1)
        {
            throw new UpdateException("Unsupported update plan schema.");
        }
        if (!PlatformInfo.IsSupported(plan.Platform))
        {
            throw new UpdateException("Unsupported plan platform.");
        }
        if (!SemanticVersion.TryParse(plan.ToVersion, out _))
        {
            throw new UpdateException("Invalid plan target version.");
        }
        if (string.IsNullOrWhiteSpace(plan.InstallDir)
            || !Directory.Exists(plan.InstallDir))
        {
            throw new UpdateException("Invalid plan installation directory.");
        }
        if (plan.Files.Count == 0)
        {
            throw new UpdateException("The plan contains no files to update.");
        }
        if (string.IsNullOrWhiteSpace(plan.Manifest)
            || string.IsNullOrWhiteSpace(plan.Signature))
        {
            throw new UpdateException("Manifest or signature is missing from the plan.");
        }

        var stagingRoot = Path.GetFullPath(plan.StagingDir);
        var stagingPrefix = stagingRoot.EndsWith(Path.DirectorySeparatorChar)
            ? stagingRoot
            : stagingRoot + Path.DirectorySeparatorChar;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in plan.Files)
        {
            if (!PathSafety.IsSafeRelativePath(file.Path))
            {
                throw new UpdateException($"Unsafe path in plan: '{file.Path}'.");
            }
            if (!seen.Add(file.Path))
            {
                throw new UpdateException($"Duplicate file in plan: '{file.Path}'.");
            }
            if (file.Mode is not ("0644" or "0755"))
            {
                throw new UpdateException($"Invalid mode in plan for '{file.Path}'.");
            }
            if (!FileHash.LooksLikeSha256(file.Sha256) || file.Size <= 0)
            {
                throw new UpdateException($"Invalid metadata in plan for '{file.Path}'.");
            }
            PathSafety.CombineSafe(plan.InstallDir, file.Path);
            var source = Path.GetFullPath(file.SourcePath);
            if (!source.StartsWith(stagingPrefix, StringComparison.Ordinal))
            {
                throw new UpdateException(
                    $"Staged file is outside the expected directory: '{file.Path}'.");
            }
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
