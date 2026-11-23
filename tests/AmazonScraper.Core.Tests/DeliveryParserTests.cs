using AmazonScraper.Core.Formatting;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Parsing;

namespace AmazonScraper.Core.Tests;

public class DeliveryParserTests
{
    [Fact]
    public void Italy_delivery_extracts_cost_time_cutoff_and_location()
    {
        const string html = """
            <span id="glow-ingress-single-line"> Il tuo indirizzo di consegna: 20121 ⌵ </span>
            <div id="mir-layout-DELIVERY_BLOCK">
              <div id="mir-layout-DELIVERY_BLOCK-slot-PRIMARY_DELIVERY_MESSAGE_LARGE">
                <span data-csa-c-delivery-price="GRATUITA"
                      data-csa-c-delivery-type="Consegna"
                      data-csa-c-delivery-time="mercoledì, 18 novembre"
                      data-csa-c-delivery-cutoff="Ordina entro 2 ore 35 min">
                  Consegna GRATUITA mercoledì, 18 novembre.
                </span>
              </div>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0CX9CT3ZF", "IT", "20121");

        var delivery = Assert.IsType<Models.DeliveryInfo>(detail.Delivery);
        Assert.Equal("20121", delivery.Location);
        Assert.Equal("IT", delivery.Country);
        Assert.Equal("20121", delivery.PostalCode);
        Assert.Equal("Consegna", delivery.Type);
        Assert.Equal("GRATUITA", delivery.Cost);
        Assert.Equal("mercoledì, 18 novembre", delivery.Time);
        Assert.Contains("Ordina entro", delivery.Cutoff);
        Assert.Contains("GRATUITA", delivery.Raw);
        Assert.Contains("DeliveryCost\tGRATUITA", TsvFormatter.Detail(detail));
    }

    [Fact]
    public void Cross_border_delivery_keeps_foreign_cost_and_country()
    {
        const string html = """
            <span id="glow-ingress-single-line"> Il tuo indirizzo di consegna: Francia ⌵ </span>
            <div id="mir-layout-DELIVERY_BLOCK">
              <div id="mir-layout-DELIVERY_BLOCK-slot-PRIMARY_DELIVERY_MESSAGE_LARGE">
                <span data-csa-c-delivery-price="a 10,52&nbsp;€"
                      data-csa-c-delivery-type="Consegna"
                      data-csa-c-delivery-time="mercoledì, 18 novembre">
                  Consegna a 10,52 € mercoledì, 18 novembre.
                </span>
              </div>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0CX9CT3ZF", "FR", "75001");

        var delivery = Assert.IsType<Models.DeliveryInfo>(detail.Delivery);
        Assert.Equal("Francia", delivery.Location);
        Assert.Equal("FR", delivery.Country);
        Assert.Equal("75001", delivery.PostalCode);
        Assert.Contains("10,52", delivery.Cost);
        Assert.Equal("mercoledì, 18 novembre", delivery.Time);
    }

    [Fact]
    public void Spanish_location_prefix_is_removed()
    {
        const string html = """
            <span id="glow-ingress-single-line"> Enviar a Madrid 28010 </span>
            <div id="mir-layout-DELIVERY_BLOCK">
              <div id="mir-layout-DELIVERY_BLOCK-slot-PRIMARY_DELIVERY_MESSAGE_LARGE">
                <span data-csa-c-delivery-price="GRATIS"
                      data-csa-c-delivery-type="Entrega"
                      data-csa-c-delivery-time="el jueves, 19 de noviembre">
                  Entrega GRATIS el jueves, 19 de noviembre.
                </span>
              </div>
            </div>
            """;

        var detail = new DetailParser(SpainMarketplace.Instance)
            .Parse(html, "B0CX9CT3ZF", "ES", "28010");

        var delivery = Assert.IsType<Models.DeliveryInfo>(detail.Delivery);
        Assert.Equal("Madrid 28010", delivery.Location);
        Assert.Equal("GRATIS", delivery.Cost);
        Assert.Equal("Entrega", delivery.Type);
    }

    [Fact]
    public void Secondary_delivery_exposes_faster_cutoff()
    {
        const string html = """
            <span id="glow-ingress-single-line"> Il tuo indirizzo di consegna: 70129 Bari ⌵ </span>
            <div id="mir-layout-DELIVERY_BLOCK">
              <div id="mir-layout-DELIVERY_BLOCK-slot-PRIMARY_DELIVERY_MESSAGE_LARGE">
                <span data-csa-c-delivery-price="GRATUITA"
                      data-csa-c-delivery-type="Consegna"
                      data-csa-c-delivery-time="venerdì, 20 novembre"
                      data-csa-c-delivery-cutoff="">
                  Consegna GRATUITA venerdì, 20 novembre.
                </span>
              </div>
              <div id="mir-layout-DELIVERY_BLOCK-slot-SECONDARY_DELIVERY_MESSAGE_LARGE">
                <span data-csa-c-delivery-price="più rapida"
                      data-csa-c-delivery-type="consegna"
                      data-csa-c-delivery-time="domani, 17 novembre"
                      data-csa-c-delivery-cutoff="Ordina entro 8 ore 56 min">
                  oppure consegna più rapida domani, 17 novembre.
                </span>
              </div>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0C9C4GHGN", "IT", "70129");

        var delivery = Assert.IsType<Models.DeliveryInfo>(detail.Delivery);
        Assert.Equal("Ordina entro 8 ore 56 min", delivery.Cutoff);
        Assert.Equal("venerdì, 20 novembre", delivery.Time);
        Assert.NotNull(delivery.Options);
        Assert.Equal(2, delivery.Options.Count);
        Assert.Equal("Ordina entro 8 ore 56 min", delivery.Options[1].Cutoff);
        Assert.Equal("domani, 17 novembre", delivery.Options[1].Time);
    }

    [Fact]
    public void Italian_update_action_is_not_part_of_the_location()
    {
        const string html = """
            <span id="glow-ingress-line1">In consegna a Bologna 40138</span>
            <span id="glow-ingress-line2">Aggiorna posizione</span>
            <div id="mir-layout-DELIVERY_BLOCK-slot-PRIMARY_DELIVERY_MESSAGE_LARGE">
              <span data-csa-c-delivery-price="GRATUITA"
                    data-csa-c-delivery-type="Consegna"
                    data-csa-c-delivery-time="mercoledì, 18 novembre">
                Consegna GRATUITA mercoledì, 18 novembre.
              </span>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0CX9CT3ZF", "IT", "40138");

        var delivery = Assert.IsType<Models.DeliveryInfo>(detail.Delivery);
        Assert.Equal("Bologna 40138", delivery.Location);
        Assert.DoesNotContain("Aggiorna", delivery.Location);
    }

    [Fact]
    public void English_update_action_is_not_part_of_the_location()
    {
        const string html = """
            <span id="glow-ingress-line1">Delivering to New York 10019</span>
            <span id="glow-ingress-line2">Update location</span>
            <div id="mir-layout-DELIVERY_BLOCK-slot-PRIMARY_DELIVERY_MESSAGE_LARGE">
              <span data-csa-c-delivery-price="FREE"
                    data-csa-c-delivery-type="Delivery"
                    data-csa-c-delivery-time="Wednesday, November 18">
                FREE delivery Wednesday, November 18.
              </span>
            </div>
            """;

        var detail = new DetailParser(UnitedStatesMarketplace.Instance)
            .Parse(html, "B0CX9CT3ZF", "US", "10019");

        var delivery = Assert.IsType<Models.DeliveryInfo>(detail.Delivery);
        Assert.Equal("New York 10019", delivery.Location);
        Assert.DoesNotContain("Update", delivery.Location);
    }

    [Fact]
    public void Contextual_location_action_suffix_is_removed()
    {
        const string html = """
            <div id="contextualIngressPtLabel_deliveryShortLine">Consegna a Bologna 40138: aggiorna luogo</div>
            <div id="mir-layout-DELIVERY_BLOCK-slot-PRIMARY_DELIVERY_MESSAGE_LARGE">
              <span data-csa-c-delivery-price="GRATUITA"
                    data-csa-c-delivery-type="Consegna"
                    data-csa-c-delivery-time="mercoledì, 18 novembre">
                Consegna GRATUITA mercoledì, 18 novembre.
              </span>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0CX9CT3ZF", "IT", "40138");

        var delivery = Assert.IsType<Models.DeliveryInfo>(detail.Delivery);
        Assert.Equal("Bologna 40138", delivery.Location);
    }

    [Fact]
    public void No_delivery_block_leaves_delivery_null()
    {
        const string html = "<html><body><span id=\"title\">Test</span></body></html>";

        var detail = new DetailParser(ItalyMarketplace.Instance).Parse(html, "B0CX9CT3ZF");

        Assert.Null(detail.Delivery);
    }
}

public class DeliveryFallbackParserTests
{
    [Fact]
    public void Cutoff_is_read_from_countdown_text_without_data_attributes()
    {
        const string html = """
            <div data-cy="delivery-block">
              <span>Order within <span id="ftCountdown">8 hrs 56 mins</span></span>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0C9C4GHGN");

        var delivery = Assert.IsType<Models.DeliveryInfo>(detail.Delivery);
        Assert.Contains("Order within", delivery.Cutoff);
        Assert.Contains("8 hrs 56 mins", delivery.Cutoff);
    }

    [Fact]
    public void Cutoff_is_read_from_localized_prompt_without_countdown_span()
    {
        const string html = """
            <div data-cy="delivery-block">
              <span>Ordina entro 8 ore 56 min per riceverlo domani.</span>
            </div>
            """;

        var detail = new DetailParser(ItalyMarketplace.Instance)
            .Parse(html, "B0C9C4GHGN");

        var delivery = Assert.IsType<Models.DeliveryInfo>(detail.Delivery);
        Assert.Contains("Ordina entro 8 ore 56 min", delivery.Cutoff);
    }
}
