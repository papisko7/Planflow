namespace PlanFlow.Infrastructure.Email;

/// <summary>
/// SMTP settings (configuration section "Email"). Defaults point at a local Mailpit catcher (localhost:1025, no login),
/// so development never sends real mail. For real delivery set Host/Port/Username/Password/FromAddress through
/// <c>dotnet user-secrets</c> or environment variables (Email__Password) — never in a committed file.
/// </summary>
public class EmailOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;

    /// <summary>True for providers that upgrade the connection with STARTTLS on port 587 (Gmail, Brevo). Leave false for local Mailpit.</summary>
    public bool UseStartTls { get; set; }

    public string? Username { get; set; }
    public string? Password { get; set; }

    public string FromAddress { get; set; } = "noreply@planflow.local";
    public string FromName { get; set; } = "PlanFlow";
}
