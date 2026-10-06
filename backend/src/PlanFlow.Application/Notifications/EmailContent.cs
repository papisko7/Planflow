namespace PlanFlow.Application.Notifications;

/// <summary>
/// A rendered message: e-mail subject plus an HTML and a plain-text body (clients that cannot show HTML fall back to the text).
/// <paramref name="Preview"/> is the one-sentence gist without greeting or footer — it becomes the body of the in-app inbox entry and the push.
/// </summary>
public record EmailContent(string Subject, string HtmlBody, string TextBody, string Preview = "");
