using FuelImport.Core.Options;
using FuelImport.Core.Services;

namespace FuelImport.Tests;

public class ConfidenceScorerTests
{
    [Fact]
    public void Calculate_AppliesValidationPenalties()
    {
        var scorer = new ConfidenceScorer(new ConfidenceOptions());

        var high = scorer.Calculate(0.9m, 0.9m, 0.9m, 0.9m, 0.9m, 0, 0);
        var low = scorer.Calculate(0.9m, 0.9m, 0.9m, 0.9m, 0.9m, 1, 2);

        Assert.True(low < high);
    }
}
