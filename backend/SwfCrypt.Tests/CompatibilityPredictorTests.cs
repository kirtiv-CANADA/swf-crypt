using SwfCrypt.Api.Data;
using SwfCrypt.Api.Services;

namespace SwfCrypt.Tests;

public class CompatibilityPredictorTests
{
    [Theory]
    [InlineData("None", CompatibilityVerdict.Resurrected)]
    [InlineData("AS1", CompatibilityVerdict.Resurrected)]
    [InlineData("AS2", CompatibilityVerdict.Resurrected)]
    [InlineData("AS3", CompatibilityVerdict.Haunted)]
    [InlineData("Mixed", CompatibilityVerdict.Haunted)]
    [InlineData("SomethingNew", CompatibilityVerdict.Haunted)]
    public void Verdict_follows_the_actionscript_generation(string actionScript, CompatibilityVerdict expected)
    {
        Assert.Equal(expected, CompatibilityPredictor.Predict(actionScript, version: 10));
    }
}
