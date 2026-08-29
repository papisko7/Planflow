using PlanFlow.Domain.Services;

namespace PlanFlow.Tests.Domain.Services;

/// <summary>
/// Unit tests for <see cref="UrgencyScoreCalculator"/>.
/// Verifies the pure, deterministic scoring formula and its clamping behavior.
/// </summary>
public class UrgencyScoreCalculatorTests
{
    [Fact]
    public void Calculate_AllZeros_ReturnsZero()
    {
        var result = UrgencyScoreCalculator.Calculate(0, 0, 0, 0, 0);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Calculate_AllOnes_ReturnsOne()
    {
        var result = UrgencyScoreCalculator.Calculate(1, 1, 1, 1, 1);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void Calculate_DeadlineWeightDominates()
    {
        var withDeadlineOnly = UrgencyScoreCalculator.Calculate(1, 0, 0, 0, 0);
        var withImpactOnly = UrgencyScoreCalculator.Calculate(0, 0, 0, 1, 0);

        Assert.True(withDeadlineOnly > withImpactOnly);
        Assert.Equal(0.35, withDeadlineOnly, precision: 5);
        Assert.Equal(0.20, withImpactOnly, precision: 5);
    }

    [Fact]
    public void Calculate_AiWeightSecondHighest()
    {
        var withAiOnly = UrgencyScoreCalculator.Calculate(0, 1, 0, 0, 0);
        var withBlockingOnly = UrgencyScoreCalculator.Calculate(0, 0, 1, 0, 0);

        Assert.True(withAiOnly > withBlockingOnly);
        Assert.Equal(0.20, withAiOnly, precision: 5);
        Assert.Equal(0.15, withBlockingOnly, precision: 5);
    }

    [Fact]
    public void Calculate_UserOverrideWeightLowest()
    {
        var withOverrideOnly = UrgencyScoreCalculator.Calculate(0, 0, 0, 0, 1);

        Assert.Equal(0.10, withOverrideOnly, precision: 5);
        Assert.True(withOverrideOnly < 0.15);
    }

    [Fact]
    public void Calculate_MixedComponents_ProperlyWeighted()
    {
        var result = UrgencyScoreCalculator.Calculate(1, 1, 1, 1, 1);

        var expected = 0.35 * 1.0 + 0.20 * 1.0 + 0.15 * 1.0 + 0.20 * 1.0 + 0.10 * 1.0;
        Assert.Equal(expected, result, precision: 5);
        Assert.Equal(1.0, result, precision: 5);
    }

    [Fact]
    public void Calculate_WeightsSum_ToOne()
    {
        var weightsSum = 0.35 + 0.20 + 0.15 + 0.20 + 0.10;
        Assert.Equal(1.0, weightsSum, precision: 5);
    }

    [Fact]
    public void Calculate_ClampsBelowZero()
    {
        var result = UrgencyScoreCalculator.Calculate(-1, -1, -1, -1, -1);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Calculate_ClampsAboveOne()
    {
        var result = UrgencyScoreCalculator.Calculate(2, 2, 2, 2, 2);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void Calculate_IndividualComponentsClamped()
    {
        var withNegatives = UrgencyScoreCalculator.Calculate(-5, -3, -2, -1, -0.5);
        var withPositives = UrgencyScoreCalculator.Calculate(2, 3, 4, 5, 6);

        Assert.Equal(0.0, withNegatives);
        Assert.Equal(1.0, withPositives);
    }

    [Fact]
    public void Calculate_PartialOverflow_Clamped()
    {
        var result = UrgencyScoreCalculator.Calculate(0.5, 0.5, 0.5, 0.5, 0.5);
        var expected = 0.35 * 0.5 + 0.20 * 0.5 + 0.15 * 0.5 + 0.20 * 0.5 + 0.10 * 0.5;

        Assert.Equal(expected, result, precision: 5);
        Assert.Equal(0.5, result, precision: 5);
    }

    [Fact]
    public void Calculate_ZeroPlusManyOnes_LessThanAllOnes()
    {
        var result1 = UrgencyScoreCalculator.Calculate(0, 1, 1, 1, 1);
        var result2 = UrgencyScoreCalculator.Calculate(1, 1, 1, 1, 1);

        Assert.True(result1 < result2);
        Assert.Equal(1.0, result2);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void Calculate_DeadlineAlone_ScalesLinearly(double deadlineScore)
    {
        var result = UrgencyScoreCalculator.Calculate(deadlineScore, 0, 0, 0, 0);
        var expected = 0.35 * deadlineScore;

        Assert.Equal(expected, result, precision: 5);
    }

    [Fact]
    public void Calculate_IsDeterministic()
    {
        var result1 = UrgencyScoreCalculator.Calculate(0.3, 0.2, 0.5, 0.7, 0.1);
        var result2 = UrgencyScoreCalculator.Calculate(0.3, 0.2, 0.5, 0.7, 0.1);

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void Calculate_TreatsAllInputsSymmetrically_WhenWeighted()
    {
        var onlyDeadline = UrgencyScoreCalculator.Calculate(1, 0, 0, 0, 0);
        var onlyAi = UrgencyScoreCalculator.Calculate(0, 1, 0, 0, 0);
        var onlyImpact = UrgencyScoreCalculator.Calculate(0, 0, 0, 1, 0);

        Assert.Equal(0.35, onlyDeadline, precision: 5);
        Assert.Equal(0.20, onlyAi, precision: 5);
        Assert.Equal(0.20, onlyImpact, precision: 5);
    }
}
