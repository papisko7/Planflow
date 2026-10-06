using System.Security.Cryptography;
using System.Text;

namespace PlanFlow.Application.Common.Security;

/// <summary>
/// Hashes a reset code together with the user id (so equal codes of two users differ) and compares in constant time.
/// A 6-digit code has only a million values, which is why the real protection is the 5-attempt lock and 15-minute
/// expiry on <c>OneTimeCode</c> — the hash just keeps a leaked database row from showing a usable code.
/// </summary>
public static class OneTimeCodeHasher
{
    public static string Hash(Guid userId, string code) => TokenHasher.Hash($"{userId:N}:{code}");

    public static bool Matches(string storedHash, Guid userId, string code) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(storedHash),
            Encoding.UTF8.GetBytes(Hash(userId, code)));
}
