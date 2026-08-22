using System.Text.RegularExpressions;

namespace DailyMate.Agent;

/// <summary>
/// LLM 키가 없을 때 전체 플로우를 구동하는 규칙 기반 엔진 (Mock 모드).
/// LLM 모드에서도 활동 감지·일정 파싱의 결정적(deterministic) 폴백으로 사용된다.
/// </summary>
public static partial class MockEngine
{
    private static readonly (string Type, Regex Rx)[] Detectors =
    [
        ("workout", new Regex(@"(헬스장에서\s*운동|헬스|운동|러닝|조깅|달리기|요가|필라테스|수영|클라이밍|등산|자전거|라이딩|축구|농구|야구|테니스|배드민턴|골프|벤치프레스|스쿼트|데드리프트|크로스핏|줄넘기|홈트)", RegexOptions.Compiled)),
        ("meeting", new Regex(@"([가-힣A-Za-z0-9 ]{0,10}(회의|미팅|스탠드업|면담|발표|세미나|컨퍼런스|브리핑))", RegexOptions.Compiled)),
        ("study",   new Regex(@"([가-힣A-Za-z0-9 ]{0,8}(공부|학습|강의|스터디|독서|책을?\s*읽|인강|과제|복습|예습|시험\s*준비|자격증))", RegexOptions.Compiled)),
        ("expense", new Regex(@"([가-힣A-Za-z0-9,\. ]{0,10}(샀다|샀어|결제|지출|\d+원|구매|쇼핑|장을?\s*봤))", RegexOptions.Compiled)),
        ("activity", new Regex(@"(여행|모임|영화|전시|산책|카페|데이트|요리|청소|빨래|게임|노래방|콘서트|공연|드라마|맛집|약속|파티|병원|미용실|드라이브|낚시|캠핑|봉사|반려견|산책로|친구.{0,6}(만났|놀았)|저녁\s*약속)", RegexOptions.Compiled)),
    ];

    public static List<DetectedSpan> Detect(string text)
    {
        var spans = new List<DetectedSpan>();
        foreach (var (type, rx) in Detectors)
        {
            foreach (Match m in rx.Matches(text))
            {
                // 이미 감지된 구간과 겹치면 과잉 감지 방지를 위해 건너뜀
                if (spans.Any(s => m.Index < s.End && m.Index + m.Length > s.Start)) continue;
                spans.Add(new DetectedSpan(m.Index, m.Index + m.Length, type));
            }
        }
        return spans.OrderBy(s => s.Start).ToList();
    }

    public static List<string> Topics(string diaryText) =>
        Detect(diaryText).Select(s => s.Type).Distinct().ToList();

    public static string QuoteFor(string diaryText, string topic)
    {
        var span = Detect(diaryText).FirstOrDefault(s => s.Type == topic);
        if (span is null) return "";
        // span이 포함된 문장 앞뒤로 잘라 인용구 생성
        var start = Math.Max(0, span.Start - 8);
        var end = Math.Min(diaryText.Length, span.End + 6);
        return $"\"{(start > 0 ? "..." : "")}{diaryText[start..end].Trim()}...\"";
    }

    public static string QuestionFor(string topic) => topic switch
    {
        "workout" => "오늘 무슨 운동 하셨어요? 중량이나 세트 수도 알려주시면 기록해둘게요 💪",
        "meeting" => "어떤 내용의 회의였나요? 회의록이나 화이트보드 사진이 있으면 올려주세요 📝",
        "study" => "어떤 부분을 공부하셨어요? 학습 기록으로 남겨드릴게요 📚",
        "expense" => "얼마를 어디에 쓰셨어요? 지출 기록으로 정리해둘게요 💸",
        _ => "그 활동에 대해 조금 더 자세히 얘기해주실 수 있어요? ✨"
    };

    public static string AckFor(string topic, string answer)
    {
        if (IsSkip(answer)) return "괜찮아요, 다음으로 넘어갈게요 🙂";
        return topic switch
        {
            "workout" when WeightRx().IsMatch(answer) => "기록 확인했어요! 꾸준한 게 최고예요 🎉 운동 기록에 저장할게요.",
            "workout" => "좋아요, 운동 기록에 저장할게요 💪",
            "meeting" => "회의 내용 정리해둘게요 📝",
            "study" => "학습 기록으로 남겨둘게요 📚",
            "expense" => "지출 내역 정리했어요 💸",
            _ => "기록해둘게요 ✨"
        };
    }

    public static bool IsSkip(string answer) =>
        Regex.IsMatch(answer.Trim(), @"^(패스|스킵|모름|몰라|넘어가|없어|글쎄|pass|skip)", RegexOptions.IgnoreCase);

    // ── 일정 파싱 ──────────────────────────────────────────
    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*kg")] private static partial Regex WeightRx();
    [GeneratedRegex(@"(\d+)\s*[x×]\s*(\d+)")] private static partial Regex SetsRx();
    [GeneratedRegex(@"(\d{1,3}(?:,\d{3})*|\d+)\s*원")] private static partial Regex AmountRx();

    private static readonly Regex TentativeRx = new(@"(할까\s*생각\s*중|생각\s*중|할지도|고민|미정|아마)", RegexOptions.Compiled);
    private static readonly Regex HourRx = new(@"(\d{1,2})\s*시", RegexOptions.Compiled);

    public static List<ScheduleDto> ParseSchedules(string text, string? forDate = null)
    {
        var date = forDate ?? DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
        var segments = Regex.Split(text, @"[,.\n]|(?<=[고중])\s+(?=[가-힣])")
            .Select(s => s.Trim()).Where(s => s.Length > 1).ToList();

        var result = new List<ScheduleDto>();
        foreach (var seg in segments)
        {
            var tentative = TentativeRx.IsMatch(seg);
            int hour = HourRx.Match(seg) is { Success: true } hm ? int.Parse(hm.Groups[1].Value)
                : seg.Contains("아침") ? 8
                : seg.Contains("오전") ? 10
                : seg.Contains("점심") ? 12
                : seg.Contains("오후") ? 14
                : seg.Contains("저녁") || seg.Contains("밤") ? 19
                : 9;
            if (seg.Contains("오후") && hour < 12) hour += 12;

            var title = seg;
            title = Regex.Replace(title, @"(그리고|그다음|그리곤)\s*", "");
            title = Regex.Replace(title, @"(아침|오전|점심|오후|저녁|밤)(에는|에|엔)?\s*", "");
            title = HourRx.Replace(title, "");
            title = Regex.Replace(title, @"(에\s*)?(갈까|할까)?\s*(생각\s*중|할지도|고민( 중)?|미정)\s*(이야|이에요)?$", "");
            title = Regex.Replace(title, @"(이\s*)?(있고|있어요?|있음|가야\s*해|해야\s*해|할\s*예정(이야)?|하기로\s*했어|일\s*거\s*같다)\s*$", "");
            title = Regex.Replace(title, @"(에|에서)\s*$", "");
            title = title.Trim(' ', '.', '!', '~');
            if (title.Length == 0) continue;

            result.Add(new ScheduleDto(
                Guid.NewGuid().ToString("N"),
                title,
                $"{date}T{hour:D2}:00:00",
                tentative ? "tentative" : "confirmed",
                "tomorrow_plan",
                false));
        }
        return result;
    }

    // ── 메타데이터 추출 ─────────────────────────────────────
    public static Dictionary<string, object> ExtractMetadata(List<string> topics, List<string> answers, string? tomorrowAnswer)
    {
        var workouts = new List<object>(); var meetings = new List<object>();
        var studies = new List<object>(); var expenses = new List<object>();

        for (var i = 0; i < topics.Count && i < answers.Count; i++)
        {
            var a = answers[i];
            if (IsSkip(a)) continue;
            switch (topics[i])
            {
                case "workout":
                    workouts.Add(new
                    {
                        exercise = Regex.Match(a, @"[가-힣A-Za-z]+(?:프레스|스쿼트|러닝|요가)?").Value is { Length: > 0 } ex ? ex : a,
                        weight = WeightRx().Match(a) is { Success: true } w ? w.Value : null,
                        sets = SetsRx().Match(a) is { Success: true } s ? s.Value : null,
                        note = a
                    });
                    break;
                case "meeting":
                    meetings.Add(new { title = a.Length > 24 ? a[..24] : a, notes = a, photoIds = Array.Empty<string>() });
                    break;
                case "study":
                    studies.Add(new { topic = a.Length > 24 ? a[..24] : a, detail = a });
                    break;
                case "expense":
                    expenses.Add(new
                    {
                        item = a,
                        amount = AmountRx().Match(a) is { Success: true } m ? long.Parse(m.Groups[1].Value.Replace(",", "")) : 0,
                        category = "기타"
                    });
                    break;
                default:
                    studies.Add(new { topic = "활동", detail = a });
                    break;
            }
        }

        return new Dictionary<string, object>
        {
            ["workouts"] = workouts,
            ["meetings"] = meetings,
            ["studies"] = studies,
            ["expenses"] = expenses,
            ["schedules"] = tomorrowAnswer is null ? new List<ScheduleDto>() : ParseSchedules(tomorrowAnswer)
        };
    }

    // ── 일기 풍부화 ─────────────────────────────────────────
    public static (string Enriched, List<string> Hashtags) Enrich(
        string raw, List<string> topics, List<string> answers)
    {
        var spans = Detect(raw);
        var enriched = raw;
        // 뒤에서부터 삽입해 오프셋이 밀리지 않도록 처리
        var inserts = new List<(int Pos, string Text)>();
        for (var i = 0; i < topics.Count && i < answers.Count; i++)
        {
            if (IsSkip(answers[i])) continue;
            var span = spans.FirstOrDefault(s => s.Type == topics[i]);
            if (span is null) continue;
            var sentenceEnd = raw.IndexOfAny(['.', '!', '\n'], span.End);
            if (sentenceEnd < 0) sentenceEnd = raw.Length; else sentenceEnd += 1;
            inserts.Add((sentenceEnd, $" **{answers[i].Trim().TrimEnd('.')}.**"));
        }
        foreach (var (pos, text) in inserts.OrderByDescending(x => x.Pos))
            enriched = enriched.Insert(pos, text);

        var tags = new List<string>();
        for (var i = 0; i < topics.Count && i < answers.Count; i++)
        {
            if (IsSkip(answers[i])) continue;
            tags.Add(topics[i] switch
            {
                "workout" => "#운동기록",
                "meeting" => "#회의",
                "study" => "#학습",
                "expense" => "#지출관리",
                _ => "#일상"
            });
            if (topics[i] == "workout" && WeightRx().Match(answers[i]) is { Success: true } w)
                tags.Add($"#{Regex.Match(answers[i], @"[가-힣]+").Value}{w.Groups[1].Value}kg");
        }
        tags.Add("#DailyMate");
        return (enriched, tags.Distinct().Take(5).ToList());
    }
}
