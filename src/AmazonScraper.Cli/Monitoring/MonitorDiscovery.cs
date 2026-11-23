using AmazonScraper.Cli.Startup;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring;

public sealed class MonitorDiscovery
{
    private readonly MonitorRegistry _registry;
    private readonly IReadOnlyList<string> _scanPaths;
    private readonly StartupRegistry _startups;

    public MonitorDiscovery(
        MonitorRegistry registry,
        IEnumerable<string> scanPaths,
        StartupRegistry? startups = null)
    {
        _registry = registry;
        _startups = startups ?? new StartupRegistry();
        _scanPaths = scanPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public MonitorDiscoveryResult Discover(bool includeStale, bool prune)
    {
        var candidates = new Dictionary<int, Candidate>();

        foreach (var entry in _registry.ReadAll())
        {
            if (entry.Pid > 0)
            {
                AddCandidate(
                    candidates,
                    new Candidate(entry, entry.PidFile, FromRegistry: true));
            }
        }

        foreach (var directory in _scanPaths)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.pid");
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var file in files)
            {
                MonitorPidInfo? info;
                try
                {
                    info = MonitorPidInfo.TryLoad(file);
                }
                catch (MonitorException)
                {
                    continue;
                }

                if (info is null || info.Pid <= 0)
                {
                    continue;
                }

                info.PidFile ??= file;
                AddCandidate(
                    candidates,
                    new Candidate(info, file, FromRegistry: false));
            }
        }

        var entries = new List<MonitorListEntry>();
        var startupDefinitions = _startups.ReadAll();
        var matchedStartupIds = new HashSet<string>(StringComparer.Ordinal);
        var stale = 0;
        var pruned = 0;
        var runningPids = candidates.Values
            .Where(candidate => MonitorProcessInspector.IsRunning(candidate.Info))
            .Select(candidate => candidate.Info.Pid)
            .ToHashSet();
        var runningInfos = candidates.Values
            .Where(candidate => runningPids.Contains(candidate.Info.Pid))
            .Select(candidate => candidate.Info)
            .ToList();

        foreach (var candidate in candidates.Values)
        {
            var info = candidate.Info;
            var pidFile = candidate.PidFile;
            var isRunning = runningPids.Contains(info.Pid);

            var savePath = info.SavePath;
            if (string.IsNullOrWhiteSpace(savePath) && !string.IsNullOrWhiteSpace(pidFile))
            {
                savePath = Path.GetDirectoryName(pidFile);
            }

            var saveFile = info.SaveFile;
            if (string.IsNullOrWhiteSpace(saveFile) && !string.IsNullOrWhiteSpace(pidFile))
            {
                saveFile = Path.GetFileNameWithoutExtension(pidFile);
            }

            var startup = FindStartup(
                startupDefinitions,
                info,
                savePath,
                saveFile);

            var superseded = !isRunning
                && runningInfos.Any(other =>
                    other.Pid != info.Pid
                    && MonitorIdentity.Same(other, info));

            if (!isRunning)
            {
                // A restart creates a new run id. Keep the list clean even
                // with --all; the next start or --prune removes the file.
                if (superseded && !prune)
                {
                    if (startup is not null)
                    {
                        matchedStartupIds.Add(startup.Id);
                    }
                    continue;
                }

                stale++;
                if (prune)
                {
                    if (candidate.FromRegistry)
                    {
                        _registry.Unregister(info.Id);
                    }
                    if (!string.IsNullOrWhiteSpace(pidFile)
                        && PidFileBelongsTo(pidFile, info))
                    {
                        TryDelete(pidFile);
                    }
                    pruned++;
                    continue;
                }

                if (!includeStale)
                {
                    continue;
                }
            }

            if (startup is not null)
            {
                matchedStartupIds.Add(startup.Id);
            }

            entries.Add(new MonitorListEntry
            {
                Id = info.Id,
                BindingId = info.BindingId,
                Pid = info.Pid,
                Status = isRunning ? "running" : "stale",
                StartedAt = info.StartedAt,
                LastSeenAt = ReadLastSeen(savePath, saveFile),
                Mode = info.Mode,
                Startup = startup?.Kind ?? info.Startup,
                StartupEnabled = startup?.Enabled
                    ?? !string.IsNullOrWhiteSpace(info.Startup),
                StartupName = startup?.Name ?? info.StartupName,
                StartupId = startup?.Id ?? info.StartupId,
                StartupConfigPath = startup?.ConfigPath,
                StartupCommandPreview = startup?.CommandPreview,
                Domains = info.Domains,
                Targets = info.Targets,
                TimerSeconds = info.TimerSeconds,
                ScheduleMode = info.ScheduleMode,
                RuntimeHash = info.RuntimeHash,
                WindowSeconds = info.WindowSeconds,
                MaxRequests = info.MaxRequests,
                MinGapSeconds = info.MinGapSeconds,
                MaxGapSeconds = info.MaxGapSeconds,
                SavePath = savePath,
                SaveFile = saveFile,
                DataFiles = info.DataFiles,
                LogPath = info.LogPath,
                PidFile = pidFile ?? info.PidFile,
            });
        }

        if (includeStale)
        {
            foreach (var startup in startupDefinitions.Where(
                         entry => !matchedStartupIds.Contains(entry.Id)))
            {
                entries.Add(new MonitorListEntry
                {
                    Id = startup.Id,
                    BindingId = startup.BindingId,
                    Pid = 0,
                    Status = "configured",
                    LastSeenAt = ReadLastSeen(
                        startup.SavePath,
                        startup.SaveFile),
                    Mode = "startup",
                    Startup = startup.Kind,
                    StartupEnabled = startup.Enabled,
                    StartupName = startup.Name,
                    StartupId = startup.Id,
                    StartupConfigPath = startup.ConfigPath,
                    StartupCommandPreview = startup.CommandPreview,
                    Domains = startup.Domains,
                    Targets = startup.Targets,
                    TimerSeconds = startup.TimerSeconds,
                    ScheduleMode = startup.ScheduleMode,
                    WindowSeconds = startup.WindowSeconds,
                    MaxRequests = startup.MaxRequests,
                    MinGapSeconds = startup.MinGapSeconds,
                    MaxGapSeconds = startup.MaxGapSeconds,
                    SavePath = startup.SavePath,
                    SaveFile = startup.SaveFile,
                    DataFiles = startup.DataFiles,
                    LogPath = startup.LogPath,
                    PidFile = Path.Combine(
                        startup.SavePath,
                        $"{startup.SaveFile}.pid"),
                });
            }
        }

        if (prune)
        {
            // Catch stale registry files that were not selected as candidates
            // (for example a recycled PID or an entry without a pidfile).
            foreach (var entry in _registry.ReadAll())
            {
                if (MonitorProcessInspector.IsRunning(entry))
                {
                    continue;
                }

                if (_registry.Unregister(entry.Id))
                {
                    pruned++;
                }
            }
        }

        return new MonitorDiscoveryResult(
            entries
                .OrderBy(entry => entry.Status switch
                {
                    "running" => 0,
                    "stale" => 1,
                    "configured" => 2,
                    _ => 3,
                })
                .ThenByDescending(entry => entry.StartedAt, StringComparer.Ordinal)
                .ToArray(),
            stale,
            pruned);
    }

    private sealed record Candidate(
        MonitorPidInfo Info,
        string? PidFile,
        bool FromRegistry);

    private static StartupDefinition? FindStartup(
        IReadOnlyList<StartupDefinition> definitions,
        MonitorPidInfo info,
        string? savePath,
        string? saveFile)
    {
        if (!string.IsNullOrWhiteSpace(info.StartupId))
        {
            var byId = definitions.FirstOrDefault(entry =>
                string.Equals(entry.Id, info.StartupId, StringComparison.Ordinal));
            if (byId is not null)
            {
                return byId;
            }
        }

        if (string.IsNullOrWhiteSpace(savePath)
            || string.IsNullOrWhiteSpace(saveFile))
        {
            return null;
        }

        return definitions.FirstOrDefault(entry =>
            StartupRegistry.MatchesSchedule(entry, savePath, saveFile));
    }

    private static string? ReadLastSeen(string? savePath, string? saveFile)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return null;
        }

        var baseName = string.IsNullOrWhiteSpace(saveFile)
            ? "amz-monitor"
            : saveFile;
        var statePath = Path.Combine(savePath, $"{baseName}.state.json");
        var state = MonitorStateStore.TryRead(statePath);
        if (state is null)
        {
            return null;
        }

        var values = new List<DateTimeOffset>();
        if (state.Targets is not null)
        {
            values.AddRange(state.Targets.Values
                .Select(target => target.LastSeenAt)
                .Where(value => value is not null)
                .Select(value => value!.Value));
        }
        if (state.Groups is not null)
        {
            values.AddRange(state.Groups.Values
                .Select(group => group.LastSeenAt)
                .Where(value => value is not null)
                .Select(value => value!.Value));
        }

        return values.Count == 0
            ? null
            : MonitorObservationFactory.FormatTimestamp(values.Max());
    }

    private static void AddCandidate(
        Dictionary<int, Candidate> candidates,
        Candidate candidate)
    {
        if (!candidates.TryGetValue(candidate.Info.Pid, out var existing))
        {
            candidates[candidate.Info.Pid] = candidate;
            return;
        }

        if (ShouldReplace(existing, candidate))
        {
            candidates[candidate.Info.Pid] = candidate with
            {
                PidFile = candidate.PidFile ?? existing.PidFile,
            };
            return;
        }

        if (existing.PidFile is null && candidate.PidFile is not null)
        {
            candidates[candidate.Info.Pid] = existing with
            {
                PidFile = candidate.PidFile,
            };
        }
    }

    private static bool ShouldReplace(Candidate existing, Candidate candidate)
    {
        var existingRunning = MonitorProcessInspector.IsRunning(existing.Info);
        var candidateRunning = MonitorProcessInspector.IsRunning(candidate.Info);
        if (candidateRunning != existingRunning)
        {
            return candidateRunning;
        }

        if (candidate.Info.ProcessStartedAt is { } candidateStarted
            && existing.Info.ProcessStartedAt is { } existingStarted)
        {
            return candidateStarted > existingStarted;
        }
        if (candidate.Info.ProcessStartedAt is not null)
        {
            return true;
        }
        if (existing.Info.ProcessStartedAt is not null)
        {
            return false;
        }

        return string.CompareOrdinal(
            candidate.Info.StartedAt,
            existing.Info.StartedAt) > 0;
    }

    private static bool PidFileBelongsTo(string pidFile, MonitorPidInfo info)
    {
        try
        {
            var current = MonitorPidInfo.TryLoad(pidFile);
            if (current is null || current.Pid != info.Pid)
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(info.Id)
                || string.IsNullOrWhiteSpace(current.Id)
                || string.Equals(info.Id, current.Id, StringComparison.Ordinal);
        }
        catch (MonitorException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
