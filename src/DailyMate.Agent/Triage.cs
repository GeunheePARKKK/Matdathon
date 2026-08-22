using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DailyMate.Agent;

public record TriageResult(
    [property: JsonPropertyName("is_workout")] bool IsWorkout,
    [property: JsonPropertyName("target")] string? Target,
    [property: JsonPropertyName("fatigue_level")] string? FatigueLevel,
    [property: JsonPropertyName("pain_areas")] string[]? PainAreas,
    [property: JsonPropertyName("fatigued_areas")] string[]? FatiguedAreas,
    [property: JsonPropertyName("heavy_preference")] bool HeavyPreference,
    [property: JsonPropertyName("volume_request")] string? VolumeRequest,
    [property: JsonPropertyName("equipment_preference")] string? EquipmentPreference,
    [property: JsonPropertyName("same_target_yesterday")] bool SameTargetYesterday,
    [property: JsonPropertyName("condition_summary")] string? ConditionSummary);

/// <summary>
/// 규칙 기반 트리아지 — LLM 없이도 헬스 코치가 동작하도록 하는 결정적 폴백.
/// "루틴 짜줘 / 운동 추천 / 어깨 운동 뭐하지" 같은 루틴 요청을 감지하고 컨디션 파라미터를 추출한다.
/// </summary>
public static partial class MockTriage
{
    [GeneratedRegex(@"(루틴|운동\s*(추천|계획|짜|뭐)|추천해\s*줘|뭐\s*(하지|할까)|프로그램\s*짜)")]
    private static partial Regex RoutineRequestRx();

    private static readonly (string Key, Regex Rx)[] TargetRx =
    [
        ("chest", new Regex(@"가슴|벤치")),
        ("back", new Regex(@"\b등\b|등\s*운동|랫|로우")),
        ("shoulders", new Regex(@"어깨\s*(운동|루틴|하|할|로)")),
        ("legs", new Regex(@"하체|다리|스쿼트|레그")),
        ("core", new Regex(@"코어|복근")),
    ];

    private static readonly (string Key, Regex Rx)[] AreaRx =
    [
        ("shoulder_front", new Regex(@"어깨")),
        ("wrist", new Regex(@"손목")),
        ("elbow", new Regex(@"팔꿈치")),
        ("lower_back", new Regex(@"허리")),
        ("hamstring", new Regex(@"햄스트링|허벅지\s*뒤")),
        ("knee", new Regex(@"무릎")),
    ];

    public static bool IsRoutineRequest(string text) => RoutineRequestRx().IsMatch(text);

    public static TriageResult Analyze(string text, IEnumerable<string> history)
    {
        var all = string.Join(" ", history.Append(text));

        string? target = TargetRx.FirstOrDefault(t => t.Rx.IsMatch(text)).Key
            ?? TargetRx.FirstOrDefault(t => t.Rx.IsMatch(all)).Key;

        var pain = new List<string>();
        var fatigued = new List<string>();
        foreach (var (key, rx) in AreaRx)
        {
            foreach (Match m in rx.Matches(all))
            {
                var tail = all[m.Index..Math.Min(all.Length, m.Index + m.Length + 8)];
                if (Regex.IsMatch(tail, @"아프|아파|통증|쑤시")) { if (!pain.Contains(key)) pain.Add(key); }
                else if (Regex.IsMatch(tail, @"결리|뻐근|피곤|힘들|무리")) { if (!fatigued.Contains(key)) fatigued.Add(key); }
            }
        }
        fatigued.RemoveAll(pain.Contains); // 통증이 우선

        var heavy = Regex.IsMatch(all, @"고중량|무겁게|헤비") && !Regex.IsMatch(all, @"고중량\s*(싫|말|X)|가볍게");
        var volume = Regex.IsMatch(all, @"길게|1시간|한\s*시간|빡세게") ? "long"
            : Regex.IsMatch(all, @"짧게|빨리|시간\s*없|가볍게") ? "short" : "normal";
        var equip = Regex.IsMatch(all, @"머신|케이블") ? "machine"
            : Regex.IsMatch(all, @"프리\s*웨이트|바벨|덤벨") ? "free" : "any";
        var fatigue = Regex.IsMatch(all, @"너무\s*피곤|녹초|과로|힘이\s*없") ? "high" : "normal";

        var summaryParts = new List<string>();
        if (pain.Count > 0) summaryParts.Add($"통증: {string.Join(",", pain)}");
        if (fatigued.Count > 0) summaryParts.Add($"결림/피로: {string.Join(",", fatigued)}");
        if (equip == "machine") summaryParts.Add("머신 위주 선호");
        if (heavy) summaryParts.Add("고중량 선호");

        return new TriageResult(
            IsRoutineRequest(text), target, fatigue,
            [.. pain], [.. fatigued], heavy, volume, equip,
            SameTargetYesterday: false,
            summaryParts.Count > 0 ? string.Join(", ", summaryParts) : "특이사항 없음");
    }
}
