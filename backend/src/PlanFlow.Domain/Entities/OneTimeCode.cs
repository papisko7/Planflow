using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// A one-time 6-digit code e-mailed to a user, either to reset a forgotten password or to confirm an e-mail address.
/// Only a hash is stored, the code expires after 15 minutes and locks after too many wrong guesses.
/// </summary>
public class OneTimeCode : BaseEntity
{
    public const int MaxAttempts = 5;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public OneTimeCodePurpose Purpose { get; set; } = OneTimeCodePurpose.PasswordReset;
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? UsedAtUtc { get; set; }

    public bool IsUsable(DateTime nowUtc) => UsedAtUtc is null && FailedAttempts < MaxAttempts && ExpiresAtUtc > nowUtc;
}
