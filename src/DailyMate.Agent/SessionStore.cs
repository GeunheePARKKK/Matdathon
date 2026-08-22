using System.Collections.Concurrent;

namespace DailyMate.Agent;

public record ChatTurn(string Role, string Content);

/// <summary>
/// Per-session conversation history (no login — session id comes from the client).
/// Keeps the most recent turns so condition info persists across routine requests.
/// </summary>
public sealed class SessionStore
{
    private const int MaxEntries = 20; // 10 user/assistant turn pairs

    private readonly ConcurrentDictionary<string, List<ChatTurn>> _sessions = new();

    public IReadOnlyList<ChatTurn> GetHistory(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return [];
        if (!_sessions.TryGetValue(sessionId, out var list)) return [];
        lock (list)
        {
            return [.. list];
        }
    }

    public void Append(string? sessionId, string role, string content)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        var list = _sessions.GetOrAdd(sessionId, _ => []);
        lock (list)
        {
            list.Add(new ChatTurn(role, content));
            if (list.Count > MaxEntries)
            {
                list.RemoveRange(0, list.Count - MaxEntries);
            }
        }
    }
}
