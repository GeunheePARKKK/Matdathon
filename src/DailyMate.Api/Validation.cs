using System.Text.RegularExpressions;

namespace DailyMate.Api;

/// <summary>쓰기 API 페이로드 서버측 검증 — 클라이언트 우회 호출에 대한 방어선.</summary>
public static partial class Validation
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")] private static partial Regex DateRx();
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?$")] private static partial Regex DateTimeRx();

    public const int MaxContentLength = 20_000;
    public const int MaxTitleLength = 100;
    public const int MaxSchedulesPerRequest = 20;
    public const int MaxHashtags = 10;
    public const int MaxPhotos = 20;

    public static string? ValidateDiary(DiaryEntry e)
    {
        if (!DateRx().IsMatch(e.Date)) return "date는 yyyy-MM-dd 형식이어야 해요.";
        if (e.RawContent.Length > MaxContentLength) return $"일기 본문은 {MaxContentLength:N0}자 이하여야 해요.";
        if (e.EnrichedContent.Length > MaxContentLength * 2) return "완성 일기가 허용 길이를 초과했어요.";
        if (e.Hashtags.Length > MaxHashtags) return $"해시태그는 {MaxHashtags}개 이하여야 해요.";
        if (e.Photos.Length > MaxPhotos) return $"사진은 {MaxPhotos}장 이하여야 해요.";
        return null;
    }

    public static string? ValidateSchedules(Schedule[] schedules)
    {
        if (schedules.Length == 0) return "등록할 일정이 없어요.";
        if (schedules.Length > MaxSchedulesPerRequest) return $"한 번에 {MaxSchedulesPerRequest}건까지 등록할 수 있어요.";
        foreach (var s in schedules)
        {
            if (string.IsNullOrWhiteSpace(s.Title)) return "일정 제목이 비어 있어요.";
            if (s.Title.Length > MaxTitleLength) return $"일정 제목은 {MaxTitleLength}자 이하여야 해요.";
            if (!DateTimeRx().IsMatch(s.Datetime)) return "일정 시각은 ISO 8601(yyyy-MM-ddTHH:mm[:ss]) 형식이어야 해요.";
            if (s.Status is not ("confirmed" or "tentative")) return "일정 상태는 confirmed 또는 tentative여야 해요.";
            if (s.Source is not ("today" or "tomorrow_plan")) return "일정 출처가 올바르지 않아요.";
        }
        return null;
    }
}
