using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Cli.Skill;
using Spectre.Console.Cli;

namespace AmazonScraper.Cli.Commands;

public sealed class SkillCommand : Command<SkillSettings>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower),
        },
    };

    public override int Execute(
        CommandContext context,
        SkillSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.Install && !settings.Check)
        {
            SkillPrinter.Print();
            return 0;
        }

        try
        {
            var result = settings.Install
                ? SkillInstaller.Install(settings.Directory, settings.Force)
                : SkillInstaller.Check(settings.Directory);

            if (settings.Json)
            {
                Console.Out.WriteLine(
                    JsonSerializer.Serialize(result, JsonOptions));
            }
            else
            {
                Console.Out.WriteLine(result.Message);
            }

            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Skill error: {ex.Message}");
            return 1;
        }
    }
}
