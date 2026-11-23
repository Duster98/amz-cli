using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class DetailSafetyWarningsTests
{
    private const string ItalianSafetyHtml = """
        <html><body>
        <div data-card-metrics-id="dp-cpsia-product-safety-warning_DetailPage_2">
          <div class="a-box a-alert a-alert-warning">
            <div class="a-box-inner a-alert-container">
              <h4 class="a-alert-heading">ATTENZIONE:</h4>
              <div class="a-alert-content">RISCHIO DI SOFFOCAMENTO -- Piccole parti. Non adatto ai bambini sotto i 3 anni.<br/></div>
            </div>
          </div>
        </div>
        <div id="pqv-important-information">
          <h2>Informazioni importanti</h2>
          <h3>Informazioni di sicurezza</h3>
          <p>Non adatto a bambini di età inferiore a 3 anni</p>
          <h3>cpsia_subject_specific_additional_warning</h3>
          <p>Avvertenza: Il prodotto contiene (un) piccolo/i magnete/i. Se ingeriti, i magneti possono attaccarsi attraverso le pareti intestinali causando gravi lesioni.</p>
          <h3>Sicurezza dei giocattoli</h3>
          <p><ol class="a-ordered-list a-vertical">
            <li><span class="a-list-item">Età consigliata: </span></li>
            <li><span class="a-list-item">not_suitable_under_3_years </span></li>
            <li><span class="a-list-item">Piccole parti. Non adatto ai bambini sotto i 3 anni. </span></li>
          </ol></p>
        </div>
        <div id="pqv-description">
          <div>Descrizione prodotto</div>
          <div>Avviso di sicurezza</div>
          <div>Non adatto a bambini di età inferiore a 3 anni</div>
        </div>
        <div class="a-alert-content">Grazie per il feedback</div>
        </body></html>
        """;

    [Fact]
    public void Parses_safety_warnings_and_removes_duplicates()
    {
        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(ItalianSafetyHtml, "B0FR4R22XB");

        Assert.Contains(
            detail.SafetyWarnings,
            warning => warning.Contains("RISCHIO DI SOFFOCAMENTO", StringComparison.Ordinal));
        Assert.Contains(
            detail.SafetyWarnings,
            warning => warning.Contains("magnete", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            detail.SafetyWarnings,
            warning => warning.Contains("Grazie per il feedback", StringComparison.Ordinal));
        Assert.DoesNotContain(
            detail.SafetyWarnings,
            warning => warning.Contains(
                "not_suitable_under_3_years",
                StringComparison.Ordinal));
        Assert.Equal(
            detail.SafetyWarnings.Count,
            detail.SafetyWarnings
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.Single(
            detail.SafetyWarnings,
            warning => warning.Contains(
                "soffocamento",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Returns_empty_list_when_no_warning_is_present()
    {
        const string html = """
            <html><body>
            <div class="a-alert-content">Grazie per il feedback</div>
            </body></html>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0TEST0000");

        Assert.Empty(detail.SafetyWarnings);
    }

    [Fact]
    public void Parses_english_cpsia_warning()
    {
        const string html = """
            <div data-card-metrics-id="dp-cpsia-product-safety-warning_DetailPage_1">
              <div class="a-alert-content">WARNING: CHOKING HAZARD -- Small parts. Not for children under 3 years.</div>
            </div>
            """;

        var detail = new DetailParser(UnitedStatesMarketplace.Instance)
            .Parse(html, "B0TEST0001");

        Assert.Contains(
            detail.SafetyWarnings,
            warning => warning.Contains("CHOKING HAZARD", StringComparison.Ordinal));
    }

    [Fact]
    public void Merges_spanish_magnet_warnings()
    {
        const string html = """
            <div id="pqv-important-information">
              <h3>cpsia_subject_specific_additional_warning</h3>
              <p>Advertencia: Este producto contiene imanes pequeños. Los imanes ingeridos pueden causar lesiones.</p>
              <p>Este producto contiene imanes pequeños. Si se tragan, los imanes pueden causar lesiones graves. Acudir inmediatamente al médico.</p>
            </div>
            """;

        var detail = new DetailParser(SpainMarketplace.Instance)
            .Parse(html, "B0TEST0003");

        Assert.Single(detail.SafetyWarnings);
        Assert.Contains("lesiones graves", detail.SafetyWarnings[0]);
    }

    [Fact]
    public void Ignores_titles_and_headings_that_mention_magnetic_or_safety()
    {
        const string html = """
            <div id="pqv-description">
              <div>GAN V100 Leap, Speed Cube 3x3 Magnetic Puzzle for Adults</div>
              <div>Safety Warning</div>
              <div>Warning: This product contains small magnet(s). Swallowed magnets can cause serious injuries.</div>
            </div>
            """;

        var detail = new DetailParser(UnitedKingdomMarketplace.Instance)
            .Parse(html, "B0TEST0004");

        Assert.Single(detail.SafetyWarnings);
        Assert.Contains("small magnet", detail.SafetyWarnings[0]);
    }

    [Fact]
    public void Keeps_choking_and_age_warnings_separate()
    {
        const string html = """
            <div data-card-metrics-id="dp-cpsia-product-safety-warning_DetailPage_2">
              <div class="a-alert-content">RISCHIO DI SOFFOCAMENTO -- Piccole parti. Non adatto ai bambini sotto i 3 anni.</div>
            </div>
            <div id="pqv-important-information">
              <h3>cpsia_subject_specific_additional_warning</h3>
              <p>Avvertenza: Non adatto a bambini di età inferiore a 3 anni. Da usare sotto la sorveglianza di un adulto</p>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0TEST0005");

        Assert.Equal(2, detail.SafetyWarnings.Count);
        Assert.Contains(
            detail.SafetyWarnings,
            warning => warning.Contains("SOFFOCAMENTO", StringComparison.Ordinal));
        Assert.Contains(
            detail.SafetyWarnings,
            warning => warning.Contains("sorveglianza", StringComparison.Ordinal));
    }

    [Fact]
    public void Keeps_the_longer_variant_of_the_same_hazard()
    {
        const string html = """
            <div id="pqv-important-information">
              <h3>cpsia_subject_specific_additional_warning</h3>
              <p>Avvertenza: Il prodotto contiene magneti. Se ingeriti, cercare assistenza medica.</p>
              <p>Avvertenza: Il prodotto contiene magneti di piccole dimensioni. Se ingeriti, i magneti possono attaccarsi lungo l'intestino e causare gravi lesioni. Cercare immediatamente assistenza medica.</p>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0TEST0002");

        Assert.Single(detail.SafetyWarnings);
        Assert.Contains("gravi lesioni", detail.SafetyWarnings[0]);
    }
}
