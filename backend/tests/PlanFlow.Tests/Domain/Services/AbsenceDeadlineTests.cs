using PlanFlow.Domain.Services;

namespace PlanFlow.Tests.Domain.Services;

/// <summary>Out-of-office time taken off a deadline - merged, clipped to the window and capped at 7 days.</summary>
public class AbsenceDeadlineTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc);

    private static AbsenceSpan Span(double fromDays, double toDays) => new(Now.AddDays(fromDays), Now.AddDays(toDays));

    [Fact]
    public void Deduction_NoAbsences_IsZero()
    {
        Assert.Equal(TimeSpan.Zero, AbsenceDeadline.Deduction(null, Now, Now.AddDays(5)));
        Assert.Equal(TimeSpan.Zero, AbsenceDeadline.Deduction(Array.Empty<AbsenceSpan>(), Now, Now.AddDays(5)));
    }

    [Fact]
    public void Deduction_AbsenceInsideTheWindow_IsItsLength()
    {
        Assert.Equal(TimeSpan.FromDays(2), AbsenceDeadline.Deduction(new[] { Span(2, 4) }, Now, Now.AddDays(5)));
    }

    [Fact]
    public void Deduction_AbsenceThatStartedBeforeNow_CountsOnlyWhatIsLeft()
    {
        Assert.Equal(TimeSpan.FromDays(1), AbsenceDeadline.Deduction(new[] { Span(-3, 1) }, Now, Now.AddDays(5)));
    }

    [Fact]
    public void Deduction_AbsenceThatRunsPastTheDeadline_IsCutAtTheDeadline()
    {
        Assert.Equal(TimeSpan.FromDays(2), AbsenceDeadline.Deduction(new[] { Span(3, 30) }, Now, Now.AddDays(5)));
    }

    [Fact]
    public void Deduction_AbsenceAfterTheDeadlineOrAlreadyOver_IsIgnored()
    {
        Assert.Equal(TimeSpan.Zero, AbsenceDeadline.Deduction(new[] { Span(6, 8), Span(-5, -1) }, Now, Now.AddDays(5)));
    }

    [Fact]
    public void Deduction_OverlappingAbsences_AreCountedOnce()
    {
        var spans = new[] { Span(1, 3), Span(2, 4), Span(2.5, 3.5) };

        Assert.Equal(TimeSpan.FromDays(3), AbsenceDeadline.Deduction(spans, Now, Now.AddDays(10)));
    }

    [Fact]
    public void Deduction_SeparateAbsences_AreAdded()
    {
        var spans = new[] { Span(1, 2), Span(4, 6) };

        Assert.Equal(TimeSpan.FromDays(3), AbsenceDeadline.Deduction(spans, Now, Now.AddDays(10)));
    }

    [Fact]
    public void Deduction_AMonthOfLeave_IsCappedAtSevenDays()
    {
        Assert.Equal(TimeSpan.FromDays(7), AbsenceDeadline.Deduction(new[] { Span(0, 30) }, Now, Now.AddDays(40)));
    }

    [Fact]
    public void Deduction_EmptyWindow_IsZero()
    {
        Assert.Equal(TimeSpan.Zero, AbsenceDeadline.Deduction(new[] { Span(0, 3) }, Now, Now));
        Assert.Equal(TimeSpan.Zero, AbsenceDeadline.Deduction(new[] { Span(0, 3) }, Now.AddDays(2), Now));
    }

    [Fact]
    public void Covers_IncludesTheStartAndExcludesTheEnd()
    {
        var spans = new[] { Span(1, 2) };

        Assert.True(AbsenceDeadline.Covers(spans, Now.AddDays(1)));
        Assert.True(AbsenceDeadline.Covers(spans, Now.AddDays(1.5)));
        Assert.False(AbsenceDeadline.Covers(spans, Now.AddDays(2)));
        Assert.False(AbsenceDeadline.Covers(spans, Now));
        Assert.False(AbsenceDeadline.Covers(null, Now));
    }
}
