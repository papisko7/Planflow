using System.Net;
using System.Text;

namespace PlanFlow.Application.Notifications;

/// <summary>
/// The one HTML frame every PlanFlow e-mail uses. All user-supplied text (task titles, names) is HTML-encoded here,
/// so a task called "&lt;script&gt;" can never inject markup into somebody else's inbox.
/// </summary>
internal static class EmailLayout
{
    private const string SettingsHint = "You can choose which e-mails you receive in the PlanFlow app: Settings → Notifications.";
    private const string MandatoryHint = "This is a security / account e-mail, so it is always sent.";

    public static EmailContent Build(
        string subject,
        string recipientName,
        IReadOnlyList<string> paragraphs,
        string? highlight = null,
        bool mandatory = false)
    {
        var footer = mandatory ? MandatoryHint : SettingsHint;

        var html = new StringBuilder();
        html.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;max-width:520px;margin:0 auto;color:#1f2937\">");
        html.Append("<h2 style=\"color:#4f46e5;margin-bottom:4px\">PlanFlow</h2>");
        html.Append($"<p>Hi {WebUtility.HtmlEncode(recipientName)},</p>");
        foreach (var paragraph in paragraphs)
        {
            html.Append($"<p>{WebUtility.HtmlEncode(paragraph)}</p>");
        }

        if (highlight is not null)
        {
            html.Append("<p style=\"font-size:28px;letter-spacing:6px;font-weight:700;background:#eef2ff;padding:12px 16px;border-radius:8px;text-align:center\">");
            html.Append(WebUtility.HtmlEncode(highlight));
            html.Append("</p>");
        }

        html.Append($"<p style=\"color:#6b7280;font-size:12px;margin-top:24px\">{WebUtility.HtmlEncode(footer)}</p></div>");

        var text = new StringBuilder();
        text.AppendLine($"Hi {recipientName},").AppendLine();
        foreach (var paragraph in paragraphs)
        {
            text.AppendLine(paragraph).AppendLine();
        }

        if (highlight is not null)
        {
            text.AppendLine(highlight).AppendLine();
        }

        text.AppendLine(footer);

        return new EmailContent(subject, html.ToString(), text.ToString(), paragraphs.Count > 0 ? paragraphs[0] : subject);
    }
}
