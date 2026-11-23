using AmazonScraper.Cli.Startup;

namespace AmazonScraper.Cli.Tests;

public class StartupRegistryTests
{
    [Fact]
    public void Save_FindById_and_ReadAll_round_trip()
    {
        using var directory = new TempDirectory();
        var registry = new StartupRegistry(directory.Root);
        var definition = Definition(
            directory.Root,
            "amz-monitor");

        registry.Save(definition);

        var byId = registry.FindById(definition.Id);
        Assert.NotNull(byId);
        Assert.Equal(definition.Id, byId!.Id);
        Assert.Equal(definition.SavePath, byId.SavePath);
        Assert.Equal(definition.Arguments, byId.Arguments);
        Assert.Single(registry.ReadAll());
        Assert.True(File.Exists(Path.Combine(directory.Root, $"{definition.Id}.json")));
    }

    [Fact]
    public void FindBySchedule_matches_path_and_file()
    {
        using var directory = new TempDirectory();
        var registry = new StartupRegistry(directory.Root);
        var definition = Definition(directory.Root, "amz-monitor");
        registry.Save(definition);

        var found = registry.FindBySchedule(
            definition.SavePath + Path.DirectorySeparatorChar,
            "amz-monitor");

        Assert.NotNull(found);
        Assert.Equal(definition.Id, found!.Id);
        Assert.Null(registry.FindBySchedule(definition.SavePath, "other"));
    }

    [Fact]
    public void Delete_removes_the_entry()
    {
        using var directory = new TempDirectory();
        var registry = new StartupRegistry(directory.Root);
        var definition = Definition(directory.Root, "amz-monitor");
        registry.Save(definition);

        registry.Delete(definition.Id);

        Assert.Null(registry.FindById(definition.Id));
        Assert.Empty(registry.ReadAll());
    }

    [Fact]
    public void ReadAll_ignores_malformed_entries()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Root, "broken.json"), "{not json");
        var registry = new StartupRegistry(directory.Root);

        Assert.Empty(registry.ReadAll());
    }

    [Fact]
    public void ReadAll_of_a_missing_directory_returns_empty()
    {
        using var directory = new TempDirectory();
        var registry = new StartupRegistry(Path.Combine(directory.Root, "missing"));

        Assert.Empty(registry.ReadAll());
    }

    [Fact]
    public void Save_sets_user_only_permissions_on_linux()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TempDirectory();
        var registry = new StartupRegistry(directory.Root);
        var definition = Definition(directory.Root, "amz-monitor");
        registry.Save(definition);

        var mode = File.GetUnixFileMode(
            Path.Combine(directory.Root, $"{definition.Id}.json"));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            mode & (UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead
                | UnixFileMode.OtherRead));
    }

    private static StartupDefinition Definition(string savePath, string saveFile)
    {
        var id = StartupIdentity.CreateId(savePath, saveFile);
        return new StartupDefinition
        {
            Id = id,
            Kind = "systemd-user",
            Name = $"amz-monitor-{id}.service",
            Enabled = true,
            CreatedAt = "2026-11-15T22:00:00.000Z",
            Executable = "/usr/bin/amz-cli",
            WorkingDirectory = savePath,
            SavePath = StartupIdentity.NormalizePath(savePath),
            SaveFile = saveFile,
            Domains = ["it"],
            Targets = 1,
            TimerSeconds = 3600,
            DataFiles = [Path.Combine(savePath, $"{saveFile}.items.jsonl")],
            LogPath = Path.Combine(savePath, $"{saveFile}.log"),
            Arguments = ["monitor", "--config", "/tmp/monitor.json"],
            CommandPreview = "monitor --config /tmp/monitor.json",
        };
    }
}
