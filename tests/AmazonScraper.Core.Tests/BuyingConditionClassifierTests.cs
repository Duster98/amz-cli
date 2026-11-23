using AmazonScraper.Core.Models;

namespace AmazonScraper.Core.Tests;

public class BuyingConditionClassifierTests
{
    [Theory]
    [InlineData("Nuovo", "new", "")]
    [InlineData("新品", "new", "")]
    [InlineData("Usato", "used", "")]
    [InlineData("中古商品 - 良い", "used", "good")]
    [InlineData("Ricondizionato: premium", "refurbished", "premium")]
    [InlineData("Ricondizionato: eccellente", "refurbished", "excellent")]
    [InlineData("Ricondizionato: buono", "refurbished", "good")]
    [InlineData("Ricondizionato: accettabile", "refurbished", "acceptable")]
    [InlineData("Refurbished - Excellent", "refurbished", "excellent")]
    [InlineData("Renewed: Good", "refurbished", "good")]
    [InlineData("Generalüberholt - Sehr gut", "refurbished", "excellent")]
    [InlineData("Reconditionné - Très bon", "refurbished", "excellent")]
    [InlineData("Reacondicionado - Muy bueno", "refurbished", "excellent")]
    [InlineData("整備済み品 - プレミアム", "refurbished", "premium")]
    [InlineData("Usato - come nuovo", "used", "excellent")]
    [InlineData("", "", "")]
    public void Classifies_type_and_renewed_grade(
        string value,
        string expectedType,
        string expectedGrade)
    {
        var result = BuyingConditionClassifier.Parse(value);

        Assert.Equal(expectedType, result.ConditionType);
        Assert.Equal(expectedGrade, result.ConditionGrade);
    }
}
