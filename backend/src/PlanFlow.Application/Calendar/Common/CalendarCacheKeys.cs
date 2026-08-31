namespace PlanFlow.Application.Calendar.Common;

public static class CalendarCacheKeys
{
    public static string OAuthState(string state) => $"oauth:google:state:{state}";
}
