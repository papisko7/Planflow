using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PlanFlow.Application.Calendar.Common;

/// <summary>
/// Fingerprints of what Google sent. The <c>[planflow]</c> tag stays in Google, so every later sync would find it again and overwrite
/// the impact/effort the user has since edited in the app. The tag is therefore applied only when the title + description fingerprint
/// changes, i.e. when the text in Google really changed. The same idea tells whether Google changed any other field the user may also
/// edit on an imported item: see <see cref="ComputeFields"/>.
/// </summary>
public static class ExternalContentHash
{
    /// <summary>SHA-256 of the raw (unstripped) title and description, as 64 hex characters.</summary>
    public static string Compute(string? title, string? description)
    {
        var bytes = Encoding.UTF8.GetBytes($"{title}\n{description}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>
    /// SHA-256 of every value Google holds that the app lets the user edit as well (title, time, place, colour...). Google counts as having
    /// changed the item when this fingerprint differs from the one stored at the previous sync; what then happens to an edit waiting in the
    /// app (Google's version replaces it, or it stays and is sent) is decided by the sync job from the two "updated at" times.
    /// </summary>
    public static string ComputeFields(params object?[] values)
    {
        var text = string.Join('\u001f', values.Select(value => value switch
        {
            null => string.Empty,
            DateTime date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
