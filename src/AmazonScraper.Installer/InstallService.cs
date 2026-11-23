using AmazonScraper.Update;

namespace AmazonScraper.Installer;

public sealed record InstallResult(
    bool Success,
    bool AlreadyInstalled,
    bool Cancelled,
    string Version,
    string Platform,
    string InstallDir,
    IReadOnlyList<string> Files);

public sealed class InstallService
{
    public async Task<InstallResult> InstallLatestAsync(
        InstallerOptions options,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var platform = PlatformInfo.Detect();
        if (!PlatformInfo.IsSupported(platform))
        {
            throw new UpdateException(
                $"Unsupported platform: {platform}. " +
                "Supported platforms are linux-x64 and win-x64.");
        }

        var installDir = InstallerPaths.ResolveInstallDirectory(options.Directory);
        var existing = InstallManifest.TryRead(installDir);
        EnsureTargetCanBeUsed(installDir, existing, options);

        using var api = new UpdateApiClient(currentVersion: InstallerInfo.Version);
        var envelope = await api.GetLatestAsync(
                platform,
                options.Channel,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new UpdateException(
                $"No {options.Channel} release is available for {platform}.");

        ReleaseSignatureVerifier.VerifyOrThrow(
            envelope.Manifest,
            envelope.Signature,
            envelope.SigningKeyId);
        var manifest = ReleaseManifest.Parse(envelope.Manifest);

        if (!string.Equals(manifest.Platform, platform, StringComparison.Ordinal)
            || !string.Equals(manifest.Platform, envelope.Platform, StringComparison.Ordinal))
        {
            throw new UpdateException(
                $"The release is for {manifest.Platform}, not {platform}.");
        }
        if (!string.Equals(manifest.Channel, options.Channel, StringComparison.Ordinal)
            || !string.Equals(manifest.Channel, envelope.Channel, StringComparison.Ordinal))
        {
            throw new UpdateException(
                $"The release is on channel {manifest.Channel}, not {options.Channel}.");
        }
        if (!string.Equals(manifest.Version, envelope.Version, StringComparison.Ordinal))
        {
            throw new UpdateException(
                "The manifest version does not match the release.");
        }

        if (existing is not null
            && !options.Force
            && SemanticVersion.TryParse(existing.Version, out var existingVersion)
            && existingVersion == manifest.ParsedVersion)
        {
            if (!options.NoPath)
            {
                ApplyPathIntegration(installDir, options);
            }

            return new InstallResult(
                true,
                true,
                false,
                manifest.Version,
                manifest.Platform,
                installDir,
                existing.Files);
        }

        if (existing is not null
            && !options.Force
            && SemanticVersion.TryParse(existing.Version, out var installed)
            && installed.CompareTo(manifest.ParsedVersion) > 0)
        {
            throw new UpdateException(
                $"The installed version ({existing.Version}) is newer " +
                $"than the current release ({manifest.Version}). Use --force to replace it.");
        }

        PrintPlan(manifest, installDir, options);
        if (options.DryRun)
        {
            return new InstallResult(
                true,
                false,
                false,
                manifest.Version,
                manifest.Platform,
                installDir,
                manifest.Files.Select(file => file.Path).ToList());
        }

        if (!options.Yes && !Confirm(manifest, installDir))
        {
            return new InstallResult(
                false,
                false,
                true,
                manifest.Version,
                manifest.Platform,
                installDir,
                []);
        }

        InstallerProcessGuard.EnsureNotRunning(installDir);

        var urls = envelope.DownloadUrls;
        var stagingDirectory = Path.Combine(
            UpdatePaths.StagingDirectory(),
            $"install-{Guid.NewGuid():N}");
        var downloadsDirectory = UpdatePaths.DownloadsDirectory();
        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(downloadsDirectory);

        try
        {
            using var downloader = new ReleaseDownloader();
            var download = await downloader.DownloadAsync(
                    envelope.ReleaseId,
                    manifest,
                    urls,
                    stagingDirectory,
                    downloadsDirectory,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            return await InstallFromStagingAsync(
                    manifest,
                    download.Files,
                    installDir,
                    options,
                    verify: true,
                    stagingDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    public async Task<InstallResult> InstallFromStagingAsync(
        ReleaseManifest manifest,
        IReadOnlyList<StagedFile> stagedFiles,
        string installDir,
        InstallerOptions options,
        bool verify,
        string? stagingDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(installDir);
        var rollbackDirectory = Path.Combine(
            Path.GetDirectoryName(installDir)
                ?? Path.GetTempPath(),
            ".amz-cli-install-rollback-" + Guid.NewGuid().ToString("N"));
        UpdatePaths.MarkHidden(rollbackDirectory);

        SwapResult? swap = null;
        try
        {
            swap = FileSwap.Apply(
                installDir,
                stagedFiles,
                rollbackDirectory,
                cancellationToken);

            if (verify)
            {
                var executable = Path.Combine(
                    installDir,
                    PlatformInfo.ExecutableName(manifest.Platform));
                var result = await UpdateVerifier.VerifyAsync(
                        executable,
                        manifest.Version,
                        manifest.Platform,
                        requireCurl: false,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!result.Success)
                {
                    throw new UpdateException(
                        $"The installed binary failed its self-test: {result.Detail}");
                }
            }

            var installManifest = InstallManifest.Create(
                installDir,
                manifest.Version,
                manifest.Platform,
                manifest.Channel,
                manifest.Files.Select(file => file.Path),
                manifest.PublishedAt);
            installManifest.Write(installDir);

            if (!options.NoPath)
            {
                ApplyPathIntegration(installDir, options);
            }

            FileSwap.Commit(swap);
            swap = null;

            return new InstallResult(
                true,
                false,
                false,
                manifest.Version,
                manifest.Platform,
                installDir,
                installManifest.Files);
        }
        catch (OperationCanceledException)
        {
            Rollback(swap);
            throw;
        }
        catch (Exception)
        {
            Rollback(swap);
            throw;
        }
        finally
        {
            if (stagingDirectory is not null)
            {
                TryDeleteDirectory(stagingDirectory);
            }
        }
    }

    public Task<InstallResult> UninstallAsync(
        InstallerOptions options,
        CancellationToken cancellationToken)
    {
        var installDir = InstallerPaths.ResolveInstallDirectory(options.Directory);
        var manifest = InstallManifest.TryRead(installDir);
        if (manifest is null)
        {
            if (!options.Force)
            {
                throw new UpdateException(
                    $"No recognized installation in '{installDir}' " +
                    "(install.json missing). Use --force to override.");
            }

            manifest = new InstallManifest
            {
                Version = "unknown",
                Platform = PlatformInfo.Detect(),
                InstallDir = installDir,
                Files = [],
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (options.DryRun)
        {
            if (!options.Quiet)
            {
                Console.WriteLine(
                    $"Dry-run uninstall: {installDir} ({manifest.Files.Count} file).");
            }
            return Task.FromResult(new InstallResult(
                true,
                false,
                false,
                manifest.Version,
                manifest.Platform,
                installDir,
                manifest.Files));
        }

        InstallerProcessGuard.EnsureNotRunning(installDir);

        if (!options.NoPath)
        {
            InstallerPathIntegration.RemoveWindowsPath(installDir, options.Quiet);
            InstallerPathIntegration.RemoveLinuxSymlink(installDir);
            InstallerPathIntegration.RemoveApplicationShortcuts();
        }

        foreach (var relative in manifest.Files)
        {
            try
            {
                var path = PathSafety.CombineSafe(installDir, relative);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Continue removing the rest; report only the final result.
            }
        }

        TryDeleteFile(Path.Combine(installDir, "install.json"));

        if (options.Purge)
        {
            TryDeleteDirectory(installDir);
        }
        else
        {
            RemoveEmptyDirectories(installDir);
        }

        return Task.FromResult(new InstallResult(
            true,
            false,
            false,
            manifest.Version,
            manifest.Platform,
            installDir,
            manifest.Files));
    }

    private static void EnsureTargetCanBeUsed(
        string installDir,
        InstallManifest? existing,
        InstallerOptions options)
    {
        if (!Directory.Exists(installDir))
        {
            return;
        }

        var hasEntries = Directory.EnumerateFileSystemEntries(installDir).Any();
        if (existing is null && hasEntries && !options.Force)
        {
            throw new UpdateException(
                $"The directory '{installDir}' is not empty and does not contain " +
                "a recognized installation. Use --force to install anyway.");
        }
    }

    private static void ApplyPathIntegration(
        string installDir,
        InstallerOptions options)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                InstallerPathIntegration.AddWindowsPath(installDir, options.Quiet);
            }
            else if (OperatingSystem.IsLinux())
            {
                InstallerPathIntegration.CreateLinuxSymlink(
                    installDir,
                    options.Force,
                    options.Quiet);
            }

            InstallerPathIntegration.CreateApplicationShortcuts(
                installDir,
                options.Quiet);
        }
        catch (Exception ex)
        {
            if (!options.Quiet)
            {
                Console.Error.WriteLine(
                    $"Warning: unable to configure PATH/symlink: {ex.Message}");
            }
        }
    }

    private static void PrintPlan(
        ReleaseManifest manifest,
        string installDir,
        InstallerOptions options)
    {
        if (options.Quiet)
        {
            return;
        }

        Console.WriteLine("AMZ CLI installer");
        Console.WriteLine($"Version: {manifest.Version} ({manifest.Channel})");
        Console.WriteLine($"Platform: {manifest.Platform}");
        Console.WriteLine($"Destination: {installDir}");
        Console.WriteLine(
            $"Files: {manifest.Files.Count} " +
            $"({FormatSize(manifest.Package?.Size ?? manifest.Files.Sum(file => file.DownloadSize))})");
    }

    private static bool Confirm(ReleaseManifest manifest, string installDir)
    {
        if (Console.IsInputRedirected)
        {
            throw new UpdateException(
                "Non-interactive input: use --yes to install.");
        }

        Console.Write(
            $"Installare {manifest.Version} in '{installDir}'? [y/N] ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        return answer is "y" or "yes" or "s" or "si" or "sì";
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private static void Rollback(SwapResult? swap)
    {
        if (swap is null)
        {
            return;
        }

        try
        {
            FileSwap.Rollback(swap);
        }
        catch (Exception)
        {
            // Preserve the original error; a backup/rollback warning is printed
            // by the caller if needed.
        }
    }

    private static void RemoveEmptyDirectories(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory
                     .EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception)
            {
                // Best effort.
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Best effort.
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
