using TestSolution.Core;

namespace TestSolution.Services;

public class AuditService : IAuditLogger
{
    private readonly List<AuditLog> _logs = new();

    public Task LogAsync(string action, string userId, string details)
    {
        _logs.Add(new AuditLog
        {
            Action = action,
            UserId = userId,
            Details = details,
            Timestamp = DateTime.UtcNow
        });
        return Task.CompletedTask;
    }

    public List<AuditLog> GetRecentLogs(int count) => _logs.OrderByDescending(l => l.Timestamp).Take(count).ToList();
    public List<AuditLog> GetLogsByUser(string userId) => _logs.Where(l => l.UserId == userId).ToList();
    public List<AuditLog> GetLogsByAction(string action) => _logs.Where(l => l.Action == action).ToList();
    public void ClearLogs() => _logs.Clear();
}
