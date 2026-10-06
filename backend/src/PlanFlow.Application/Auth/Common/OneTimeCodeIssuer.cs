using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Common.Security;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Auth.Common;

/// <summary>
/// Creates the 6-digit codes behind "reset my password" and "confirm my e-mail". The code itself is returned once (to be
/// e-mailed); only its hash is stored.
/// </summary>
public static class OneTimeCodeIssuer
{
    public const int ValidMinutes = 15;

    /// <summary>A second request inside this window is ignored, so an endpoint cannot be used to flood somebody's inbox.</summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Adds a fresh code to the context (the caller saves) and cancels any earlier open code of the same purpose.
    /// Returns null when the user asked again within the cooldown (unless <paramref name="ignoreCooldown"/>), in which case nothing is added.
    /// </summary>
    public static async Task<string?> IssueAsync(
        IApplicationDbContext context, Guid userId, OneTimeCodePurpose purpose, CancellationToken cancellationToken, bool ignoreCooldown = false)
    {
        var nowUtc = DateTime.UtcNow;
        var openCodes = await context.OneTimeCodes
            .Where(c => c.UserId == userId && c.Purpose == purpose && c.UsedAtUtc == null)
            .ToListAsync(cancellationToken);

        if (!ignoreCooldown && openCodes.Any(c => c.CreatedAtUtc > nowUtc - Cooldown))
        {
            return null;
        }

        // Only the newest code is valid: asking again cancels the previous one.
        foreach (var old in openCodes)
        {
            old.UsedAtUtc = nowUtc;
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        context.OneTimeCodes.Add(new OneTimeCode
        {
            UserId = userId,
            Purpose = purpose,
            CodeHash = OneTimeCodeHasher.Hash(userId, code),
            ExpiresAtUtc = nowUtc.AddMinutes(ValidMinutes)
        });
        return code;
    }

    /// <summary>
    /// Checks a typed code against the newest open code of this purpose. A wrong guess is counted (and saved) so five misses
    /// lock the code. Returns the code row when it matches, otherwise null — the caller answers with one generic message.
    /// </summary>
    public static async Task<OneTimeCode?> TryConsumeAsync(
        IApplicationDbContext context, Guid userId, OneTimeCodePurpose purpose, string typedCode, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var code = await context.OneTimeCodes
            .Where(c => c.UserId == userId && c.Purpose == purpose && c.UsedAtUtc == null)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (code is null || !code.IsUsable(nowUtc))
        {
            return null;
        }

        if (!OneTimeCodeHasher.Matches(code.CodeHash, userId, typedCode))
        {
            code.FailedAttempts++;
            await context.SaveChangesAsync(cancellationToken);
            return null;
        }

        code.UsedAtUtc = nowUtc;
        return code;
    }
}
