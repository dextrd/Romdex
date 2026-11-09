using Romdex.Client;

namespace Romdex.Web;

public static class Format
{
    public static string TaskLabel(TaskInfo task) => task.State switch
    {
        TaskState.Queued when task.Paused => "Paused",
        TaskState.Queued => "Waiting",
        TaskState.Running => "Running",
        TaskState.Succeeded => "Done",
        TaskState.Cancelled => "Cancelled",
        _ => "Failed",
    };

    public static string TaskBadge(TaskInfo task) => task.State switch
    {
        TaskState.Queued when task.Paused => "neutral",
        TaskState.Succeeded => "owned",
        TaskState.Failed => "missing",
        TaskState.Cancelled => "neutral",
        TaskState.Queued => "active waiting",
        _ => "active",
    };

    /// <summary>How long ago, roughly: "just now", "5 min ago", "3 h ago", or the date.</summary>
    public static string Ago(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        return age.TotalMinutes < 1 ? "just now"
            : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min ago"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours} h ago"
            : utc.ToLocalTime().ToString("d MMM");
    }

    /// <summary>How soon, roughly: "in 5 min", "in 3 h", or the date.</summary>
    public static string Until(DateTime utc)
    {
        var left = utc - DateTime.UtcNow;
        return left.TotalMinutes < 1 ? "any minute"
            : left.TotalHours < 1 ? $"in {(int)left.TotalMinutes} min"
            : left.TotalDays < 1 ? $"in {(int)left.TotalHours} h {left.Minutes} min"
            : $"on {utc.ToLocalTime():d MMM}";
    }

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} bytes",
    };

    public static string Count(int n, string one, string many) => $"{n:N0} {(n == 1 ? one : many)}";
}
