using System.Security.Cryptography;
using System.Text;

namespace PlanFlow.Application.Common.Security;

/// <summary>
/// One-way hash for refresh tokens at rest (a stolen DB row shouldn't hand over a usable token).
/// Plain SHA-256 is enough here — unlike passwords, a refresh token is already a high-entropy
/// random value, so it doesn't need PBKDF2's deliberate slowness.
/// </summary>
public static class TokenHasher
{
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
