using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Web;

public sealed class WebResumeSettings : CommandSettings
{
    [CommandOption("--state <FILE>")]
    [Description("Path to the Web resume state prepared by the updater")]
    public string? State { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(State))
        {
            return ValidationResult.Error("--state is required.");
        }
        if (!File.Exists(State))
        {
            return ValidationResult.Error($"Resume state not found: {State}");
        }
        return ValidationResult.Success();
    }
}

public sealed class WebResumeCommand : AsyncCommand<WebResumeSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        WebResumeSettings settings,
        CancellationToken cancellationToken)
    {
        var resume = WebResumeStateStore.TryRead(settings.State);
        if (resume is null)
        {
            Console.Error.WriteLine(
                $"Invalid Web resume state: {settings.State}");
            return 1;
        }
        return await WebHostRunner
            .RunResumeAsync(resume, cancellationToken)
            .ConfigureAwait(false);
    }
}
