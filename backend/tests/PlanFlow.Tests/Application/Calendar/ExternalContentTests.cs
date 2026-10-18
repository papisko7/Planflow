using PlanFlow.Application.Calendar.Common;
using Xunit;

namespace PlanFlow.Tests.Application.Calendar;

/// <summary>The [planflow] tag stays in Google, so what we store must not contain it, and we must notice when Google's text really changes.</summary>
public class ExternalContentTests
{
    [Theory]
    [InlineData("Agenda\n[planflow] impact=8 effort=2h\nBring laptop", "Agenda\nBring laptop")]
    [InlineData("[planflow] impact=8", null)]                                  // nothing else in the text
    [InlineData("Agenda\n\n[planflow] impact=8\n", "Agenda")]                  // the blank line it leaves behind goes too
    [InlineData("Line one\r\n[PLANFLOW] effort=1h\r\nLine two", "Line one\nLine two")]  // any case, Windows line breaks
    [InlineData("Nothing special here", "Nothing special here")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void StripTagLines_RemovesOnlyTheTagLine(string? input, string? expected) =>
        Assert.Equal(expected, PlanflowTagParser.StripTagLines(input));

    [Fact]
    public void Hash_IsStableForTheSameTextAndChangesWhenEitherPartChanges()
    {
        var hash = ExternalContentHash.Compute("Review [planflow] impact=5", "Agenda");

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, ExternalContentHash.Compute("Review [planflow] impact=5", "Agenda"));
        Assert.NotEqual(hash, ExternalContentHash.Compute("Review [planflow] impact=6", "Agenda"));
        Assert.NotEqual(hash, ExternalContentHash.Compute("Review [planflow] impact=5", "Agenda 2"));
    }

    [Fact]
    public void Hash_CoversTheRawTextNotTheCleanedOne()
    {
        // Two texts that look the same once the tag is stripped must still differ - the tag itself is what changed.
        Assert.NotEqual(
            ExternalContentHash.Compute("Review", "Notes"),
            ExternalContentHash.Compute("Review", "Notes\n[planflow] impact=9"));
    }
}
