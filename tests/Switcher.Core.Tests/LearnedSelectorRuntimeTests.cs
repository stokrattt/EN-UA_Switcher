using Switcher.Core;

namespace Switcher.Core.Tests;

public sealed class LearnedSelectorRuntimeTests
{
    [Fact]
    public void BuildSelectorFeatures_KnownPair_ReturnsVector()
    {
        SelectorFeatureVector? vector = CorrectionHeuristics.BuildSelectorFeatures(
            "цщкві",
            "words",
            CorrectionDirection.UaToEn);

        Assert.NotNull(vector);
        Assert.Equal(CorrectionDirection.UaToEn, vector!.Direction);
        Assert.Equal("цщкві", vector.Original);
        Assert.Equal("words", vector.Converted);
        Assert.True(vector.TargetScore > vector.SourceScore);
    }

    [Fact]
    public void Evaluate_WithoutModel_ReturnsNull()
    {
        string variableName = "SWITCHER_SELECTOR_MODEL";
        string? previous = Environment.GetEnvironmentVariable(variableName);
        string tempModelPath = Path.Combine(Path.GetTempPath(), $"selector-missing-{Guid.NewGuid():N}.json");

        try
        {
            Environment.SetEnvironmentVariable(variableName, tempModelPath);

            SelectorFeatureVector vector = CorrectionHeuristics.BuildSelectorFeatures(
                "цщкві",
                "words",
                CorrectionDirection.UaToEn)!;

            LearnedSelectorDecision? decision = LearnedSelectorRuntime.Evaluate(vector);
            Assert.Null(decision);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previous);
        }
    }
}
