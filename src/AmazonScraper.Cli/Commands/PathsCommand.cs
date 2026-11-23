using AmazonScraper.Cli.Diagnostics;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class PathsCommand : Command<PathsSettings>
{
    public override int Execute(
        CommandContext context,
        PathsSettings settings,
        CancellationToken cancellationToken)
    {
        var result = AppPathCatalog.Build(new AppPathCatalogOptions
        {
            IncludeMissing = settings.All,
            IncludeInstall = settings.Install,
            AdditionalSavePaths = settings.SavePaths,
        });

        if (settings.Json)
        {
            Console.Out.WriteLine(PathTreeRenderer.ToJson(result));
        }
        else
        {
            Console.Out.Write(
                PathTreeRenderer.Render(
                    result,
                    settings.Verbose,
                    settings.Ascii));
        }

        return 0;
    }
}
