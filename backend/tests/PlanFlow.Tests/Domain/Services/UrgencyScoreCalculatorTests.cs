using PlanFlow.Domain.Services;

namespace PlanFlow.Tests.Domain.Services;

/// <summary>
/// Unit tests for <see cref="UrgencyScoreCalculator"/>.
/// Verifies the pure, deterministic scoring formula, its floors, the unrated-impact rule and clamping.
/// Argument order: (deadline, blocking, impact, userOverride, deadlineImminent).
/// </summary>
public class UrgencyScoreCalculatorTests
{
    [Fact]
    public void Calculate_AllZeros_ReturnsZero()
    {
        var result = UrgencyScoreCalculator.Calculate(0, 0, 0, 0);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Calculate_AllOnes_ReturnsOne()
    {
        var result = UrgencyScoreCalculator.Calculate(1, 1, 1, 1);
        Assert.Equal(1.0, result, precision: 5);
    }

    [Fact]
    public void Calculate_DeadlineWeightDominates()
    {
        var withDeadlineOnly = UrgencyScoreCalculator.Calculate(1, 0, 0, 0);
        var withImpactOnly = UrgencyScoreCalculator.Calculate(0, 0, 1, 0);

        Assert.True(withDeadlineOnly > withImpactOnly);
        Assert.Equal(0.45, withDeadlineOnly, precision: 5);
        Assert.Equal(0.25, withImpactOnly, precision: 5);
    }

    [Fact]
    public void Calculate_UserOverrideWeightLowest()
    {
        var withOverrideOnly = UrgencyScoreCalculator.Calculate(0, 0, 0, 1);

        Assert.Equal(0.10, withOverrideOnly, precision: 5);
        Assert.True(withOverrideOnly < 0.20);
    }

    [Fact]
    public void Calculate_WeightsSum_ToOne()
    {
        var weightsSum = 0.45 + 0.20 + 0.25 + 0.10;
        Assert.Equal(1.0, weightsSum, precision: 5);
    }

    [Fact]
    public void Calculate_ClampsBelowZero()
    {
        var result = UrgencyScoreCalculator.Calculate(-1, -1, -1, -1);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Calculate_ClampsAboveOne()
    {
        var result = UrgencyScoreCalculator.Calculate(2, 2, 2, 2);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void Calculate_IndividualComponentsClamped()
    {
        var withNegatives = UrgencyScoreCalculator.Calculate(-5, -3, -2, -1);
        var withPositives = UrgencyScoreCalculator.Calculate(2, 3, 4, 5);

        Assert.Equal(0.0, withNegatives);
        Assert.Equal(1.0, withPositives);
    }

    [Fact]
    public void Calculate_HalfOfEverything_ReturnsHalf()
    {
        // 0.5 everywhere: the weighted sum is 0.5 and the inheritance floor (0.5) does not raise it.
        var result = UrgencyScoreCalculator.Calculate(0.5, 0.5, 0.5, 0.5);

        Assert.Equal(0.5, result, precision: 5);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void Calculate_DeadlineAlone_ScalesLinearly(double deadlineScore)
    {
        var result = UrgencyScoreCalculator.Calculate(deadlineScore, 0, 0, 0);

        Assert.Equal(0.45 * deadlineScore, result, precision: 5);
    }

    [Fact]
    public void Calculate_IsDeterministic()
    {
        var result1 = UrgencyScoreCalculator.Calculate(0.3, 0.5, 0.7, 0.1);
        var result2 = UrgencyScoreCalculator.Calculate(0.3, 0.5, 0.7, 0.1);

        Assert.Equal(result1, result2);
    }

    // ---- Unrated impact ----

    [Fact]
    public void Calculate_UnratedImpact_RedistributesItsWeightInsteadOfCountingZero()
    {
        // Deadline 0.6 only: rated-as-zero would give 0.27, unrated shares the 0.25 -> 0.27 / 0.75 = 0.36.
        var rated = UrgencyScoreCalculator.Calculate(0.6, 0, 0.0, 0);
        var unrated = UrgencyScoreCalculator.Calculate(0.6, 0, null, 0);

        Assert.Equal(0.27, rated, precision: 5);
        Assert.Equal(0.36, unrated, precision: 5);
    }

    [Fact]
    public void Calculate_UnratedImpact_AllOtherFactorsMax_StillReachesOne()
    {
        var result = UrgencyScoreCalculator.Calculate(1, 1, null, 1);

        Assert.Equal(1.0, result, precision: 5);
    }

    // ---- Imminent-deadline floor ----

    [Fact]
    public void Calculate_ImminentDeadline_ScoreNeverBelowFloor()
    {
        // Deadline today but impact 0: the plain weighted sum is only 0.45.
        var result = UrgencyScoreCalculator.Calculate(1, 0, 0, 0, deadlineImminent: true);

        Assert.Equal(UrgencyScoreCalculator.ImminentDeadlineFloor, result, precision: 5);
    }

    [Fact]
    public void Calculate_ImminentDeadline_BeatsUndatedTaskWithMaxImpactOverrideAndBlocking()
    {
        var dueToday = UrgencyScoreCalculator.Calculate(1, 0, 0, 0, deadlineImminent: true);
        var undatedButEverythingElse = UrgencyScoreCalculator.Calculate(0, 0.5, 1, 1);

        Assert.True(dueToday > undatedButEverythingElse);
    }

    [Fact]
    public void Calculate_FloorDoesNotLowerAHigherScore()
    {
        var result = UrgencyScoreCalculator.Calculate(1, 1, 1, 1, deadlineImminent: true);

        Assert.Equal(1.0, result, precision: 5);
    }

    // ---- Priority inheritance (blocking) ----

    [Fact]
    public void Calculate_BlockerIsAtLeastAsUrgentAsInheritedValue()
    {
        // No deadline, no impact, but it blocks something worth 0.8 -> it can't score below 0.8.
        var result = UrgencyScoreCalculator.Calculate(0, 0.8, 0, 0);

        Assert.Equal(0.8, result, precision: 5);
    }

    [Fact]
    public void Calculate_NothingBlocked_NoInheritance()
    {
        var result = UrgencyScoreCalculator.Calculate(0, 0, 0, 0);

        Assert.Equal(0.0, result);
    }

    // ---- Blocked penalty ----

    [Fact]
    public void ApplyBlockedPenalty_BlockedTask_ScoreIsHalved()
    {
        Assert.Equal(0.4, UrgencyScoreCalculator.ApplyBlockedPenalty(0.8, isBlocked: true), precision: 5);
    }

    [Fact]
    public void ApplyBlockedPenalty_NotBlocked_ScoreUnchanged()
    {
        Assert.Equal(0.8, UrgencyScoreCalculator.ApplyBlockedPenalty(0.8, isBlocked: false), precision: 5);
    }
}
