namespace AmazonScraper.Update;

/// <summary>
/// Extra information used when an update is requested from the local Web
/// interface. The helper restarts the Web process after applying the files so
/// the already-open page can reconnect to the same port and session.
/// </summary>
public sealed record WebUpdateContext(
    string ResumePath,
    bool ForceMonitorRestart = true);
