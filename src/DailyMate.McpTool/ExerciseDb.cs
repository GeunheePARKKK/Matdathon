using System.Text.Json;
using System.Text.Json.Serialization;

namespace DailyMate.McpTool;

public record ExerciseDefaults(
    [property: JsonPropertyName("sets")] int Sets,
    [property: JsonPropertyName("reps")] int Reps,
    [property: JsonPropertyName("rpe")] int Rpe,
    [property: JsonPropertyName("rest_sec")] int RestSec);

public record Exercise(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("pattern")] string Pattern,
    [property: JsonPropertyName("equipment")] string Equipment,
    [property: JsonPropertyName("joint_load")] string[] JointLoad,
    [property: JsonPropertyName("default")] ExerciseDefaults Default,
    [property: JsonPropertyName("alternatives")] string[] Alternatives);

/// <summary>Seed exercise database loaded from Data/exercises.json.</summary>
public sealed class ExerciseDb
{
    private readonly Dictionary<string, List<Exercise>> _byTarget;

    public ExerciseDb(IHostEnvironment env)
    {
        var path = Path.Combine(env.ContentRootPath, "Data", "exercises.json");
        var json = File.ReadAllText(path);
        _byTarget = JsonSerializer.Deserialize<Dictionary<string, List<Exercise>>>(json)
                    ?? throw new InvalidOperationException("exercises.json is empty or invalid.");
    }

    public IReadOnlyCollection<string> Targets => _byTarget.Keys;

    public IReadOnlyList<Exercise> GetByTarget(string target) =>
        _byTarget.TryGetValue(target.ToLowerInvariant(), out var list) ? list : [];

    /// <summary>Finds an exercise by exact name across all targets (used to resolve alternatives).</summary>
    public Exercise? FindByName(string name) =>
        _byTarget.Values.SelectMany(x => x).FirstOrDefault(e => e.Name == name);
}
