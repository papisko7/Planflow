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
}
