using System.Security.Cryptography;
using System.Text;

namespace PlanFlow.Application.Calendar.Common;

/// <summary>
/// Fingerprint of what Google sent as title + description. The <c>[planflow]</c> tag stays in Google (it is read-only to us),
/// so every later sync would find it again and overwrite the impact/effort the user has since edited in the app.
/// The tag is therefore applied only when this fingerprint changes, i.e. when the text in Google really changed.
/// </summary>
public static class ExternalContentHash
{
    /// <summary>SHA-256 of the raw (unstripped) title and description, as 64 hex characters.</summary>
    public static string Compute(string? title, string? description)
    {
        var bytes = Encoding.UTF8.GetBytes($"{title}\n{description}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
