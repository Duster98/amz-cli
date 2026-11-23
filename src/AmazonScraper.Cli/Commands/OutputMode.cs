namespace AmazonScraper.Cli.Commands;

public enum OutputMode
{
    Table,
    Json,
    Tsv,
}

public static class OutputModeSelector
{
    public static OutputMode Pick(bool json, bool tkOpt)
    {
        if (json)
        {
            return OutputMode.Json;
        }
        return tkOpt ? OutputMode.Tsv : OutputMode.Table;
    }
}
