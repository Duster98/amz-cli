namespace AmazonScraper.Cli.Startup;

public interface IStartupBackend
{
    string Kind { get; }

    string NameFor(string id);

    bool IsAvailable(out string? error);

    void Install(StartupDefinition definition);

    void Start(StartupDefinition definition);

    void Stop(StartupDefinition definition);

    void Restart(StartupDefinition definition) => Start(definition);

    bool Remove(StartupDefinition definition);

    bool Exists(StartupDefinition definition);
}
