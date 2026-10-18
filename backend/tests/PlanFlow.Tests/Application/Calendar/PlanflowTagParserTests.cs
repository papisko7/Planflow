using PlanFlow.Application.Calendar.Common;
using Xunit;

namespace PlanFlow.Tests.Application.Calendar;

public class PlanflowTagParserTests
{
    [Fact]
    public void ImpactAndEffort_AreReadFromTheTagLine()
    {
        var tag = PlanflowTagParser.Parse("Sprint review", "Agenda below\n[planflow] impact=8 effort=2h\nBring laptop");

        Assert.NotNull(tag);
        Assert.Equal(8, tag.ImpactScore);
        Assert.Equal(2, tag.EffortHours);
    }

    [Theory]
    [InlineData("effort=90min", 1.5)]
    [InlineData("effort=90m", 1.5)]
    [InlineData("effort: 3", 3)]          // no unit = hours
    [InlineData("effort=1,5h", 1.5)]      // decimal comma
    [InlineData("effort=2d", 48)]
    [InlineData("effort=1w", 168)]
    [InlineData("effort=1mo", 720)]       // a month is 30 days, as in the app
    public void Effort_UnderstandsTheAppUnits(string body, double expectedHours)
    {
        var tag = PlanflowTagParser.Parse(null, $"[planflow] {body}");

        Assert.Equal(expectedHours, tag!.EffortHours!.Value, precision: 6);
    }

    [Fact]
    public void KeysAndTagAreCaseInsensitive_AndColonWorksLikeEquals()
    {
        var tag = PlanflowTagParser.Parse("[PlanFlow] IMPACT: 4 Effort=30MIN");

        Assert.Equal(4, tag!.ImpactScore);
        Assert.Equal(0.5, tag.EffortHours!.Value, precision: 6);
    }

    [Fact]
    public void TagInTheTitle_IsFound_TitleWinsPerField_AndDescriptionFillsTheRest()
    {
        var tag = PlanflowTagParser.Parse("Demo [planflow] impact=9", "[planflow] impact=2 effort=3h");

        Assert.Equal(9, tag!.ImpactScore);   // both give impact: the title wins
        Assert.Equal(3, tag.EffortHours);    // only the description gives effort
    }

    [Fact]
    public void OnlyTheTagLineIsRead_FieldsOnOtherLinesAreIgnored()
    {
        var tag = PlanflowTagParser.Parse(null, "[planflow] effort=1h\nimpact=9");

        Assert.Null(tag!.ImpactScore);
        Assert.Equal(1, tag.EffortHours);
    }

    [Theory]
    [InlineData("impact=11")]
    [InlineData("impact=100")]
    [InlineData("impact=abc")]
    [InlineData("effort=0h")]
    [InlineData("effort=99999h")]
    public void OutOfRangeOrUnreadableValues_AreIgnoredNotErrors(string body)
    {
        var tag = PlanflowTagParser.Parse(null, $"[planflow] {body}");

        Assert.NotNull(tag); // the tag exists …
        Assert.Null(tag.ImpactScore); // … but yielded nothing usable
        Assert.Null(tag.EffortHours);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A normal description with impact=8 but no tag")]
    public void NoTag_ReturnsNull(string? text)
    {
        Assert.Null(PlanflowTagParser.Parse("Plain title", text));
    }

    [Fact]
    public void StripFromTitle_RemovesTheTagLine()
    {
        Assert.Equal("Sprint review", PlanflowTagParser.StripFromTitle("Sprint review [planflow] impact=8 effort=2h"));
        Assert.Equal("Plain", PlanflowTagParser.StripFromTitle("Plain"));
    }

    [Fact]
    public void ToPlainText_TurnsGoogleHtmlIntoLines_SoTheTagCanBeFound()
    {
        var plain = PlanflowTagParser.ToPlainText("Agenda&nbsp;item<br>[planflow] impact=7 effort=1h<br/><b>Bring</b> laptop");

        Assert.Equal("Agenda item\n[planflow] impact=7 effort=1h\nBring laptop", plain);
        Assert.Equal(7, PlanflowTagParser.Parse(null, plain)!.ImpactScore);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("<br>")]
    public void ToPlainText_EmptyBecomesNull(string? html)
    {
        Assert.Null(PlanflowTagParser.ToPlainText(html));
    }
}
