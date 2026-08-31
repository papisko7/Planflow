using Microsoft.AspNetCore.DataProtection;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Infrastructure.Security;

/// <summary>
/// Field-level encryption for OAuth tokens at rest, using ASP.NET Core Data Protection
/// (AES-256-CBC + HMAC-SHA256 authenticated encryption under the hood) rather than a hand-rolled
/// AES routine — it manages key generation/rotation/versioning for us, which is exactly the
/// "equivalent secure vault strategy" called for by Phase 4A instead of a bespoke crypto primitive.
/// The purpose string ("PlanFlow.CalendarTokens.v1") scopes this protector so a key compromise in
/// one feature can't be used to decrypt ciphertext produced for another.
/// </summary>
public class DataProtectionTokenEncryptionService : ITokenEncryptionService
{
    private const string Purpose = "PlanFlow.CalendarTokens.v1";

    private readonly IDataProtector _protector;

    public DataProtectionTokenEncryptionService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public string Encrypt(string plaintext) => _protector.Protect(plaintext);

    public string Decrypt(string ciphertext) => _protector.Unprotect(ciphertext);
}
