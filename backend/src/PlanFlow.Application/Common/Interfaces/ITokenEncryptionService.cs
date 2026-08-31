namespace PlanFlow.Application.Common.Interfaces;

/// <summary>
/// Encrypts/decrypts OAuth tokens before they touch the database (field-level encryption at rest
/// for <see cref="PlanFlow.Domain.Entities.CalendarIntegration"/>). Implemented in Infrastructure
/// with ASP.NET Core Data Protection (Phase 4A) so Application handlers never see a raw key.
/// </summary>
public interface ITokenEncryptionService
{
    string Encrypt(string plaintext);

    string Decrypt(string ciphertext);
}
