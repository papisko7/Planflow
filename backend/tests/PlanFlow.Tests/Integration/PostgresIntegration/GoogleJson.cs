using System.Text.Json;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>Response bodies of the Google Calendar and Google Tasks REST APIs, in the shape the real clients read.</summary>
internal static class GoogleJson
{
    public static string Events(params string[] events) => $$"""{ "items": [{{string.Join(",", events)}}] }""";

    public static string Event(
        string id, string title, DateTime startUtc, DateTime endUtc, string? description = null, string? location = null,
        string? colorId = null, string status = "confirmed", int[]? reminderMinutes = null)
    {
        var item = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["summary"] = title,
            ["status"] = status,
            ["start"] = new { dateTime = startUtc.ToString("o") },
            ["end"] = new { dateTime = endUtc.ToString("o") }
        };
        if (description is not null) item["description"] = description;
        if (location is not null) item["location"] = location;
        if (colorId is not null) item["colorId"] = colorId;
        if (reminderMinutes is not null)
        {
            item["reminders"] = new { useDefault = false, overrides = reminderMinutes.Select(m => new { method = "popup", minutes = m }) };
        }
        return JsonSerializer.Serialize(item);
    }

    public static string TaskLists(params (string Id, string Title)[] lists) =>
        JsonSerializer.Serialize(new { items = lists.Select(l => new { id = l.Id, title = l.Title }) });

    public static string Tasks(params string[] tasks) => $$"""{ "items": [{{string.Join(",", tasks)}}] }""";

    /// <param name="due">A calendar day; Google sends it as midnight UTC.</param>
    public static string Task(string id, string title, DateTime? due = null, string? notes = null, string status = "needsAction", string? parent = null)
    {
        var item = new Dictionary<string, object?> { ["id"] = id, ["title"] = title, ["status"] = status };
        if (due is { } day) item["due"] = day.ToString("yyyy-MM-dd'T'00:00:00.000'Z'");
        if (notes is not null) item["notes"] = notes;
        if (parent is not null) item["parent"] = parent;
        return JsonSerializer.Serialize(item);
    }
}
