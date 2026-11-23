using AmazonScraper.Cli.Skill;

namespace AmazonScraper.Cli.Tests;

public class SkillDocumentTests
{
    [Fact]
    public void Skill_is_embedded_with_frontmatter()
    {
        var markdown = SkillDocument.Markdown;

        Assert.False(string.IsNullOrWhiteSpace(markdown));
        Assert.StartsWith("---", markdown);
        Assert.Contains("name: amz-cli", markdown);
        Assert.Contains("description:", markdown);
    }

    [Fact]
    public void Skill_documents_commands_options_and_domains()
    {
        var markdown = SkillDocument.Markdown;

        var required = new[]
        {
            "# AMZ CLI",
            "## Domains",
            "## Commands",
            "### `amz-cli search",
            "### `amz-cli deals",
            "### `amz-cli details",
            "### `amz-cli monitor",
            "### `amz-cli update",
            "### `amz-cli --skill`",
            "amz-cli domains",
            "amz-cli self-test",
            "amz-cli --version",
            "--domain",
            "--timeout",
            "--width",
            "--transport",
            "--browser",
            "--user-agent",
            "--browser-headers",
            "--cookie",
            "--save-html",
            "--json",
            "--tk-opt",
            "--html",
            "--page",
            "--pages",
            "--page-delay",
            "--delivery",
            "--deals",
            "--deal-type",
            "--category",
            "--no-ads",
            "--check",
            "--yes",
            "--channel",
            "--rollback",
            "--enable-auto",
            "--disable-auto",
            "--no-auto-update",
            "Auto-update",
            "updates/config.json",
            "AMZ_NO_AUTO_UPDATE",
            "notify-send",
            "zenity",
            "amz-cli update --status",
            "RSA-PSS",
            "GitHub",
            "bootstrap",
            "--enrich",
            "--enrich-delay",
            "--limit",
            "--compare",
            "--variants",
            "--variant-prices",
            "--variant-delay",
            "--rating",
            "--reviews",
            "--review-limit",
            "--review-images",
            "--cap",
            "--country",
            "--no-buying-options",
            "--options-delay",
            "--all-options",
            "--options-pages",
            "--smid",
            "Analyzing a single option",
            "--skill",
            "--allow-http",
            "--timer",
            "--request-delay",
            "--save-format",
            "--layout",
            "--detach",
            "--status",
            "--stop",
            "--notify",
            "--notify-on",
            "--notify-cooldown",
            "--notify-test",
            "--install-env-var",
            "--uninstall-env-var",
            "--status-env-var",
            "env-var",
            "user PATH",
            "--list",
            "--prune",
            "--verbose",
            "--startup",
            "--startup-remove",
            "Startup",
            "Task Scheduler",
            "startup-launch",
            "loginctl enable-linger",
            "--dry-run",
            "total_max",
            "triggered_rules",
            "Desktop notifications",
            "notifications",
            "BurntToast",
            "amz-notify.exe",
            "Clicking a Windows toast",
            "Spectre.Console",
            "offer_listing_id",
            "buying_options",
            "\"deal\"",
            "deal_id",
            "parent_asin",
            "selected_asin",
            "variants",
            "VariantsCount",
            "VariantsParent",
            "rating_details",
            "top_reviews",
            "amazon_choice",
            "amazon_choice_text",
            "amazon_choice_for",
            "import_fees",
            "price_basis",
            "ImportPrice",
            "ImportPriceBasis",
            "list price",
            "ImportCharges",
            "LandedTotal",
            "ImportAtCheckout",
            "Choice",
            "Overall Pick",
            "rating_value",
            "ReviewsAvailable",
            "ReviewImage",
            "DeliveryOption",
            "original_price",
            "discount",
            "Was",
            "Badge",
            "deal_ends_text",
            "deal_ends_on",
            "deal_has_countdown",
            "DealId",
            "aodAjaxMain",
            "BuyingOption",
            "Amazon.de",
            "Amazon.co.jp",
            "Amazon.es",
            "DeliveryCost",
            "cross-border",
            "CAP",
            "HTTP 503",
            "## Rate limiting",
            "## Security and privacy",
            "## Limitations",
            "publish",
        };

        foreach (var item in required)
        {
            Assert.Contains(item, markdown);
        }

        Assert.Contains("No active monitors", markdown);
        Assert.Contains("Press any key", markdown);
        Assert.Contains("Offerta a tempo", markdown);
    }

    [Fact]
    public void Skill_covers_delivery_and_pagination_rules()
    {
        var markdown = SkillDocument.Markdown;

        Assert.Contains("`--page` and `--pages` are mutually exclusive", markdown);
        Assert.Contains(
            "`--cap`/`--country` on `details` and `monitor`",
            markdown);
        Assert.Contains("`search --delivery`", markdown);
        Assert.Contains("`delivery` is omitted", markdown);
        Assert.Contains("6 seconds", markdown);
        Assert.Contains("minimum 10s", markdown);
        Assert.Contains("`monitor` is always sequential", markdown);
        Assert.Contains("`--notify-test`", markdown);
        Assert.Contains("session 0", markdown);
    }
}
