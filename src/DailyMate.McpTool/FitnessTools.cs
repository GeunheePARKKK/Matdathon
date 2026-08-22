using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DailyMate.McpTool;

[McpServerToolType]
public sealed class FitnessTools(ExerciseDb db)
{
    private static readonly string[] MachineEquipment = ["machine", "cable"];
    private static readonly string[] FreeEquipment = ["barbell", "dumbbell", "bodyweight"];
    private const int MaxPerPattern = 2;
    private const int WorkSecondsPerSet = 40;

    [McpServerTool(Name = "get_exercises")]
    [Description("부위(target)별 운동 후보 목록을 시드 DB에서 조회한다. equipment로 필터 가능.")]
    public GetExercisesResult GetExercises(
        [Description("운동 부위: chest | back | shoulders | legs | core")] string target,
        [Description("장비 필터 (선택): barbell | dumbbell | machine | cable | bodyweight")] string? equipment = null)
    {
        var list = db.GetByTarget(target);
        if (list.Count == 0)
        {
            throw new McpException(
                $"알 수 없는 부위 '{target}'. 사용 가능: {string.Join(", ", db.Targets)}");
        }

        if (!string.IsNullOrWhiteSpace(equipment))
        {
            list = [.. list.Where(e => e.Equipment.Equals(equipment, StringComparison.OrdinalIgnoreCase))];
        }

        return new GetExercisesResult(target.ToLowerInvariant(), [.. list]);
    }

    [McpServerTool(Name = "calc_intensity")]
    [Description("컨디션(피로/통증/중량·장비·볼륨 선호)으로 강도 파라미터를 결정적으로 계산한다. 통증(pain)만 전면 제외, 결림/피로(fatigue)는 강도 하향 대상.")]
    public IntensityResult CalcIntensity(
        [Description("전반적 피로도: low | normal | high")] string fatigueLevel,
        [Description("통증('아프다/통증/쑤시다') 부위 joint_load 키 배열. 해당 운동 전면 제외.")] string[] painAreas,
        [Description("고중량 선호 여부. false면 RPE 상한 7.")] bool heavyPreference,
        [Description("결림/뻐근/피로 부위 joint_load 키 배열. 제외하지 않고 RPE·세트 하향 + 머신 대체 우선.")] string[]? fatiguedAreas = null,
        [Description("운동량 요청: short(짧게·핵심만) | normal | long(1시간 이상)")] string volumeRequest = "normal",
        [Description("장비 선호: machine(머신/케이블 위주) | free(프리웨이트) | any")] string equipmentPreference = "any")
    {
        fatiguedAreas ??= [];
        volumeRequest = NormalizeVolume(volumeRequest);
        var notes = new List<string>();

        var rpeCap = heavyPreference ? 9 : 7;
        if (!heavyPreference)
        {
            notes.Add("고중량 비선호: RPE 상한 7.");
        }

        // explicit equipment preference wins; otherwise light-load preference implies machines
        var resolvedEquipment = equipmentPreference.ToLowerInvariant() switch
        {
            "machine" => "machine",
            "free" => "free",
            _ => heavyPreference ? "any" : "machine",
        };
        if (resolvedEquipment == "machine")
        {
            notes.Add("머신/케이블 우선 구성.");
        }

        var isHighFatigue = fatigueLevel.Equals("high", StringComparison.OrdinalIgnoreCase);
        var volumeMultiplier = isHighFatigue ? 0.75 : 1.0;
        var reduceAccessoryCount = isHighFatigue ? 1 : 0;
        if (isHighFatigue)
        {
            notes.Add("전신 피로도 높음: 세트 수 25% 감소, 보조운동 1개 축소.");
        }

        if (fatiguedAreas.Length > 0)
        {
            notes.Add($"결림/피로 부위({string.Join(", ", fatiguedAreas)}): 관련 운동은 제외하지 않고 RPE·세트를 낮추고 머신/저부하 대체를 우선.");
        }

        if (painAreas.Length > 0)
        {
            notes.Add($"통증 부위({string.Join(", ", painAreas)}) 관련 운동 전부 제외. 통증이 지속되면 전문의 상담을 권장합니다.");
        }

        return new IntensityResult(
            volumeMultiplier,
            rpeCap,
            [.. painAreas],
            [.. painAreas],
            [.. fatiguedAreas],
            resolvedEquipment,
            volumeRequest,
            reduceAccessoryCount,
            [.. notes]);
    }

    [McpServerTool(Name = "build_routine")]
    [Description("운동 후보와 강도 파라미터로 fitness_routine JSON(팀 계약 스키마)을 생성한다. 워밍업·소요시간 포함.")]
    public FitnessRoutine BuildRoutine(
        [Description("운동 부위: chest | back | shoulders | legs")] string target,
        [Description("볼륨 배율 (calc_intensity의 volume_multiplier)")] double volumeMultiplier,
        [Description("RPE 상한 (calc_intensity의 rpe_cap)")] int rpeCap,
        [Description("장비 선호 (calc_intensity의 equipment_preference): machine | free | any")] string equipmentPreference = "any",
        [Description("운동량 요청 (calc_intensity의 volume_request): short | normal | long")] string volumeRequest = "normal",
        [Description("결림/피로 부위 joint_load 배열 — RPE·세트 하향 + 머신 대체 우선 (제외 아님)")] string[]? fatiguedAreas = null,
        [Description("통증 부위 joint_load 배열 — 겹치면 전면 제외 + 경고")] string[]? painAreas = null,
        [Description("축소할 보조(isolation)운동 개수 (calc_intensity의 reduce_accessory_count)")] int reduceAccessoryCount = 0,
        [Description("컨디션 요약 한 줄")] string? conditionSummary = null,
        [Description("루틴 날짜 yyyy-MM-dd (기본: 오늘)")] string? date = null,
        [Description("후보 운동 이름 배열 (선택, 기본: target 부위 전체)")] string[]? candidateNames = null,
        [Description("notes에 그대로 덧붙일 추가 안내 문구 배열 (예: 전일 동일 부위 회복 경고)")] string[]? extraNotes = null)
    {
        fatiguedAreas ??= [];
        painAreas ??= [];
        extraNotes ??= [];
        volumeRequest = NormalizeVolume(volumeRequest);
        var equipPref = equipmentPreference.ToLowerInvariant();

        var candidates = candidateNames is { Length: > 0 }
            ? [.. candidateNames.Select(db.FindByName).Where(e => e is not null).Cast<Exercise>()]
            : db.GetByTarget(target);
        if (candidates.Count == 0)
        {
            throw new McpException($"'{target}' 부위의 운동 후보가 없습니다.");
        }

        var notes = new List<string>();
        var pool = new List<Exercise>();
        var reducedNames = new List<string>();

        bool OverlapsPain(Exercise e) => e.JointLoad.Any(j => painAreas.Contains(j, StringComparer.OrdinalIgnoreCase));
        bool OverlapsFatigue(Exercise e) => e.JointLoad.Any(j => fatiguedAreas.Contains(j, StringComparer.OrdinalIgnoreCase));
        bool IsMachine(Exercise e) => MachineEquipment.Contains(e.Equipment);

        foreach (var exercise in candidates)
        {
            // Rule: pain (explicit '아프다/통증') → exclude entirely
            if (OverlapsPain(exercise))
            {
                notes.Add($"{exercise.Name}: 통증 부위 부하로 제외.");
                continue;
            }

            // Rule: fatigue (결림/뻐근/피곤) → not excluded; prefer machine/low-load substitute for free-weight moves
            if (OverlapsFatigue(exercise) && !IsMachine(exercise))
            {
                var substitute = exercise.Alternatives
                    .Select(db.FindByName)
                    .FirstOrDefault(alt => alt is not null && IsMachine(alt) && !OverlapsPain(alt));
                if (substitute is not null)
                {
                    if (!pool.Contains(substitute))
                    {
                        notes.Add($"{exercise.Name} → {substitute.Name} 대체 (피로 부위 저부하 전환).");
                        pool.Add(substitute);
                    }
                    continue;
                }
            }

            if (!pool.Contains(exercise))
            {
                pool.Add(exercise);
            }
        }

        // Ordering: compound first → isolation; preferred equipment first within each group
        bool PrefersEquipment(Exercise e) => equipPref switch
        {
            "machine" => MachineEquipment.Contains(e.Equipment),
            "free" => FreeEquipment.Contains(e.Equipment),
            _ => false,
        };
        var ordered = pool
            .OrderByDescending(e => e.Type == "compound")
            .ThenByDescending(PrefersEquipment)
            .ToList();

        var (totalTarget, compoundCap) = volumeRequest switch
        {
            "short" => (3, 2),
            "long" => (8, 3),
            _ => (4, 2),
        };

        var selected = new List<Exercise>();
        var patternCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void TrySelect(Exercise e)
        {
            if (selected.Count >= totalTarget || selected.Contains(e)) return;
            if (e.Type == "compound" && selected.Count(s => s.Type == "compound") >= compoundCap) return;
            var count = patternCount.GetValueOrDefault(e.Pattern);
            if (count >= MaxPerPattern) return; // 같은 패턴 최대 2개
            patternCount[e.Pattern] = count + 1;
            selected.Add(e);
        }

        foreach (var e in ordered) TrySelect(e);

        // long인데 후보 부족 → core에서 안전한(통증·피로 부하 없음) 운동 보충
        if (volumeRequest == "long" && selected.Count < totalTarget && !target.Equals("core", StringComparison.OrdinalIgnoreCase))
        {
            var coreSafe = db.GetByTarget("core")
                .Where(e => !OverlapsPain(e) && !OverlapsFatigue(e));
            var before = selected.Count;
            foreach (var e in coreSafe) TrySelect(e);
            if (selected.Count > before)
            {
                notes.Add("운동량 확보를 위해 코어 운동을 보충했어요.");
            }
        }
        if (volumeRequest == "long" && selected.Count < totalTarget)
        {
            notes.Add("통증 부위 회피로 시간 채우기 제한적, 유산소 20분 추가 추천.");
        }

        // fatigue high → drop trailing accessory exercise(s)
        for (var i = 0; i < reduceAccessoryCount; i++)
        {
            var accessory = selected.LastOrDefault(e => e.Type == "isolation");
            if (accessory is null) break;
            selected.Remove(accessory);
            notes.Add($"{accessory.Name}: 전신 피로도 높음으로 보조운동 축소.");
        }

        // sets/rpe 계산: 피로 부하 하향 → 볼륨 배율 → long 세트 +1 → RPE 상한
        var exercises = new List<RoutineExercise>();
        foreach (var e in selected)
        {
            var sets = e.Default.Sets;
            var rpe = e.Default.Rpe;
            if (OverlapsFatigue(e))
            {
                sets -= 1;
                rpe -= 1;
                reducedNames.Add(e.Name);
            }
            sets = Math.Max(1, (int)Math.Round(sets * volumeMultiplier));
            if (volumeRequest == "long") sets += 1;
            rpe = Math.Min(rpe, rpeCap);
            exercises.Add(new RoutineExercise(e.Name, sets, e.Default.Reps, rpe, e.Default.RestSec, null));
        }
        if (reducedNames.Count > 0)
        {
            notes.Add($"{string.Join(", ", reducedNames)}: 피로 부위 부하 완화로 RPE·세트 하향.");
        }

        // 워밍업: 첫 운동을 RPE 4 · 2세트 · 가벼운 중량으로 맨 앞에 추가
        if (exercises.Count > 0)
        {
            var first = exercises[0];
            exercises.Insert(0, first with { Sets = 2, Rpe = 4, RestSec = 60, IsWarmup = true });
            notes.Insert(0, $"{first.Name} 워밍업 2세트(RPE 4, 가벼운 중량)로 시작하세요.");
        }

        var estimatedMinutes = (int)Math.Round(
            exercises.Sum(e => e.Sets * (WorkSecondsPerSet + e.RestSec)) / 60.0);

        notes.AddRange(extraNotes);
        if (painAreas.Length > 0)
        {
            notes.Add("통증이 지속되면 전문의 상담을 권장합니다.");
        }

        var routineDate = date ?? DateTime.Now.ToString("yyyy-MM-dd");
        var summary = conditionSummary ?? "특이사항 없음";
        var mainExercises = exercises.Where(e => e.IsWarmup != true).ToArray();
        var snippet = $"오늘은 {summary} 상태를 고려해 {TargetKorean(target)} 운동을 진행했다. " +
                      string.Join(", ", mainExercises.Select(e => $"{e.Name} {e.Sets}세트")) +
                      $"로 약 {estimatedMinutes}분 구성했다.";

        return new FitnessRoutine(
            "fitness_routine",
            routineDate,
            target.ToLowerInvariant(),
            summary,
            [.. exercises],
            string.Join(" ", notes),
            snippet,
            estimatedMinutes);
    }

    private static string NormalizeVolume(string volumeRequest) =>
        volumeRequest.ToLowerInvariant() is "short" or "long" ? volumeRequest.ToLowerInvariant() : "normal";

    private static string TargetKorean(string target) => target.ToLowerInvariant() switch
    {
        "chest" => "가슴",
        "back" => "등",
        "shoulders" => "어깨",
        "legs" => "하체",
        "core" => "코어",
        _ => target,
    };
}

public record GetExercisesResult(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("exercises")] Exercise[] Exercises);

public record IntensityResult(
    [property: JsonPropertyName("volume_multiplier")] double VolumeMultiplier,
    [property: JsonPropertyName("rpe_cap")] int RpeCap,
    [property: JsonPropertyName("excluded_joint_loads")] string[] ExcludedJointLoads,
    [property: JsonPropertyName("pain_areas")] string[] PainAreas,
    [property: JsonPropertyName("fatigued_areas")] string[] FatiguedAreas,
    [property: JsonPropertyName("equipment_preference")] string EquipmentPreference,
    [property: JsonPropertyName("volume_request")] string VolumeRequest,
    [property: JsonPropertyName("reduce_accessory_count")] int ReduceAccessoryCount,
    [property: JsonPropertyName("notes")] string[] Notes);

// TRD §5 contract schema — do not change field names (is_warmup / estimated_minutes are optional additions)
public record RoutineExercise(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("sets")] int Sets,
    [property: JsonPropertyName("reps")] int Reps,
    [property: JsonPropertyName("rpe")] int Rpe,
    [property: JsonPropertyName("rest_sec")] int RestSec,
    [property: JsonPropertyName("is_warmup"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsWarmup);

public record FitnessRoutine(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("condition_summary")] string ConditionSummary,
    [property: JsonPropertyName("exercises")] RoutineExercise[] Exercises,
    [property: JsonPropertyName("notes")] string Notes,
    [property: JsonPropertyName("diary_snippet")] string DiarySnippet,
    [property: JsonPropertyName("estimated_minutes")] int EstimatedMinutes);
