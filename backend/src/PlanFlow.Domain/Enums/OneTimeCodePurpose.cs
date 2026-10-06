namespace PlanFlow.Domain.Enums;

/// <summary>What a <see cref="Entities.OneTimeCode"/> may be used for. A code only works for the purpose it was issued for.</summary>
public enum OneTimeCodePurpose
{
    PasswordReset = 0,
    EmailVerification = 1
}
