namespace PlanFlow.Application.Common.Caching;

/// <summary>Centralizes cache key formats so producers (queries) and invalidators (commands) can't drift apart.</summary>
public static class CacheKeys
{
    public static readonly TimeSpan TeamTasksTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan TaskUrgencyTtl = TimeSpan.FromMinutes(1);

    public static string TeamTasks(Guid teamId) => $"team:{teamId}:tasks";

    public static string TaskUrgency(Guid taskId) => $"task:{taskId}:urgency";
}
