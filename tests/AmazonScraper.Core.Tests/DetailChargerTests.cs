using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class DetailChargerTests
{
    [Fact]
    public void Charger_not_included_widget_and_description_are_parsed()
    {
        const string html = """
            <div id="feature-bullets">
              <ul>
                <li>
                  <span class="a-list-item">CARICATORE NON INCLUSO: usa un alimentatore USB-C che già possiedi.</span>
                </li>
              </ul>
            </div>
            <span id="buffet-red-atf-content">
              <button type="button">
                <a class="a-size-base a-link-normal" href="#">Senza caricatore</a>
              </button>
              <input type="hidden" id="buffet-red-atf-is-charger-included" value="false" aria-hidden="true"/>
            </span>
            <table class="a-keyvalue prodDetTable">
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">Marca</th>
                <td class="a-size-base prodDetAttrValue">ASUS</td>
              </tr>
            </table>
            <table class="a-keyvalue prodDetTable">
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">Contenuto confezione</th>
                <td class="a-size-base prodDetAttrValue">Certificato di garanzia</td>
              </tr>
            </table>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0H25NG1JK");

        Assert.NotNull(detail.Charger);
        Assert.False(detail.Charger!.Included);
        Assert.Equal("Senza caricatore", detail.Charger.Label);
        Assert.Contains("CARICATORE NON INCLUSO", detail.Charger.Note);
        Assert.Equal("Certificato di garanzia", detail.Charger.BoxContents);
    }

    [Fact]
    public void Description_note_can_infer_charger_not_included_without_widget()
    {
        const string html = """
            <div id="feature-bullets">
              <ul>
                <li>
                  <span class="a-list-item">CARICATORE NON INCLUSO: usa un alimentatore USB-C che già possiedi.</span>
                </li>
              </ul>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0H25NG1JK");

        Assert.NotNull(detail.Charger);
        Assert.False(detail.Charger!.Included);
        Assert.Null(detail.Charger.Label);
        Assert.Contains("CARICATORE NON INCLUSO", detail.Charger.Note);
    }

    [Fact]
    public void Charger_included_widget_is_parsed()
    {
        const string html = """
            <div id="feature-bullets">
              <ul>
                <li>
                  <span class="a-list-item">CARICATORE INCLUSO: alimentatore USB-C da 65 W nella confezione.</span>
                </li>
              </ul>
            </div>
            <span id="buffet-red-atf-content">
              <a class="a-size-base a-link-normal" href="#">Caricabatterie incluso</a>
              <input type="hidden" id="buffet-red-atf-is-charger-included" value="true" aria-hidden="true"/>
            </span>
            <table class="a-keyvalue prodDetTable">
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">Marca</th>
                <td class="a-size-base prodDetAttrValue">ASUS</td>
              </tr>
            </table>
            <table class="a-keyvalue prodDetTable">
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">Contenuto confezione</th>
                <td class="a-size-base prodDetAttrValue">Notebook, caricatore USB-C, cavo</td>
              </tr>
            </table>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0H25NG1JK");

        Assert.NotNull(detail.Charger);
        Assert.True(detail.Charger!.Included);
        Assert.Equal("Caricabatterie incluso", detail.Charger.Label);
        Assert.Contains("CARICATORE INCLUSO", detail.Charger.Note);
        Assert.Equal("Notebook, caricatore USB-C, cavo", detail.Charger.BoxContents);
    }

    [Fact]
    public void Products_without_charger_information_do_not_get_a_charger_object()
    {
        const string html = """
            <div id="feature-bullets">
              <ul>
                <li>
                  <span class="a-list-item">DESIGN PREMIUM IN ALLUMINIO.</span>
                </li>
              </ul>
            </div>
            <table class="a-keyvalue prodDetTable">
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">Marca</th>
                <td class="a-size-base prodDetAttrValue">ASUS</td>
              </tr>
            </table>
            <table class="a-keyvalue prodDetTable">
              <tr>
                <th class="a-color-secondary a-size-base prodDetSectionEntry">Contenuto confezione</th>
                <td class="a-size-base prodDetAttrValue">Notebook</td>
              </tr>
            </table>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0H25NG1JK");

        Assert.Null(detail.Charger);
    }
}
