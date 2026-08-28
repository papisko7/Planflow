namespace PlanFlow.Infrastructure.Security;

/// <summary>Bound from the "Jwt" configuration section; also read directly by the Api layer to configure JwtBearer validation.</summary>
public class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}
