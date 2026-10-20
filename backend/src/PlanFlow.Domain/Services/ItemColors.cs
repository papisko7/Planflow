namespace PlanFlow.Domain.Services;

/// <summary>
/// The 24 Google Calendar colours. Only the name (key) is stored, never the hex: the client maps the name to a shade
/// that fits the light/dark theme. A null key means "Default colour" (inherits the calendar / app theme colour).
/// </summary>
public static class ItemColors
{
    public static readonly IReadOnlyList<string> Keys =
    [
        "beetroot", "cherry-blossom", "flamingo", "tomato", "tangerine", "pumpkin",
        "mango", "banana", "citron", "avocado", "pistachio", "basil",
        "sage", "eucalyptus", "peacock", "cobalt", "lavender", "blueberry",
        "wisteria", "amethyst", "grape", "cocoa", "graphite", "birch"
    ];

    private static readonly HashSet<string> KeySet = new(Keys, StringComparer.Ordinal);

    /// <summary>Null (default colour) and every palette key are valid.</summary>
    public static bool IsValid(string? key) => key is null || KeySet.Contains(key);

    /// <summary>
    /// Google's event API knows only 11 colorIds (the old palette). The other 13 colours exist only for items created in the app.
    /// </summary>
    public static string? FromGoogleColorId(string? colorId) => colorId switch
    {
        "1" => "lavender",
        "2" => "sage",
        "3" => "grape",
        "4" => "flamingo",
        "5" => "banana",
        "6" => "tangerine",
        "7" => "peacock",
        "8" => "graphite",
        "9" => "blueberry",
        "10" => "basil",
        "11" => "tomato",
        _ => null
    };

    /// <summary>
    /// The colorId Google's event API knows for a palette colour, the reverse of <see cref="FromGoogleColorId"/>. False when the colour exists
    /// only in the app (13 of the 24), so it cannot be sent to Google. The default colour (null key) gives an empty id, which clears the colour in Google.
    /// </summary>
    public static bool TryToGoogleColorId(string? key, out string? colorId)
    {
        colorId = key switch
        {
            null => string.Empty, // empty = clear the colour in Google
            "lavender" => "1",
            "sage" => "2",
            "grape" => "3",
            "flamingo" => "4",
            "banana" => "5",
            "tangerine" => "6",
            "peacock" => "7",
            "graphite" => "8",
            "blueberry" => "9",
            "basil" => "10",
            "tomato" => "11",
            _ => null
        };
        return colorId is not null;
    }
}
