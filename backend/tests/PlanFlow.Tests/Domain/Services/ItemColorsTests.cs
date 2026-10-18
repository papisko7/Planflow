using PlanFlow.Domain.Services;

namespace PlanFlow.Tests.Domain.Services;

public class ItemColorsTests
{
    [Fact]
    public void Palette_HasThe24GoogleColoursWithoutDuplicates()
    {
        Assert.Equal(24, ItemColors.Keys.Count);
        Assert.Equal(24, ItemColors.Keys.Distinct().Count());
    }

    [Theory]
    [InlineData(null, true)]            // default colour
    [InlineData("cherry-blossom", true)]
    [InlineData("graphite", true)]
    [InlineData("#ff0000", false)]      // only names are stored, never hex values
    [InlineData("Tomato", false)]       // keys are lower-case
    [InlineData("", false)]
    public void IsValid_AcceptsNullAndPaletteNamesOnly(string? key, bool expected) =>
        Assert.Equal(expected, ItemColors.IsValid(key));

    [Fact]
    public void EveryGoogleColorId_MapsToAPaletteName()
    {
        for (var id = 1; id <= 11; id++)
        {
            var key = ItemColors.FromGoogleColorId(id.ToString());
            Assert.NotNull(key);
            Assert.True(ItemColors.IsValid(key));
        }
    }

    [Theory]
    [InlineData("11", "tomato")]
    [InlineData("7", "peacock")]
    [InlineData("10", "basil")]
    [InlineData(null, null)]    // no colorId = the calendar colour
    [InlineData("12", null)]    // unknown id falls back to the default colour
    public void FromGoogleColorId_FollowsGoogleNames(string? colorId, string? expected) =>
        Assert.Equal(expected, ItemColors.FromGoogleColorId(colorId));
}
