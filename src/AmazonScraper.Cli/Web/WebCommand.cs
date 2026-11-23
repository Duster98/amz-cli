using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Web;

public sealed class WebCommand : AsyncCommand<WebSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        WebSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.Stop)
        {
            return await WebControl.StopAsync(settings.Quiet, cancellationToken)
                .ConfigureAwait(false);
        }
        if (settings.Status)
        {
            return await WebControl.StatusAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return await WebHostRunner.RunAsync(settings, cancellationToken)
            .ConfigureAwait(false);
    }
}
