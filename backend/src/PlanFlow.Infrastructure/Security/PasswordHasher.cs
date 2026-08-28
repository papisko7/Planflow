using System.Security.Cryptography;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Infrastructure.Security;

/// <summary>
/// PBKDF2 (SHA-256, 100k iterations, random 16-byte salt) password hashing, using only the
/// .NET base class library — avoids pulling in ASP.NET Core Identity just for this one primitive.
/// Stored format: "{iterations}.{saltBase64}.{hashBase64}", so the iteration count can be raised
/// later without invalidating passwords hashed under the old count.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string passwordHash, string password)
    {
        var parts = passwordHash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[1]);
        var expectedHash = Convert.FromBase64String(parts[2]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
