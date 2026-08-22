using DailyMate.Api;

namespace DailyMate.Tests;

public class ValidationTests
{
    private static DiaryEntry Diary(string date = "2026-08-23", string raw = "일기") =>
        new(date, raw, "", [], [], null, "");

    [Fact]
    public void 올바른_일기는_통과한다() =>
        Assert.Null(Validation.ValidateDiary(Diary()));

    [Fact]
    public void 잘못된_날짜_형식은_거부한다() =>
        Assert.NotNull(Validation.ValidateDiary(Diary(date: "2026/08/23")));

    [Fact]
    public void 본문_길이_초과는_거부한다() =>
        Assert.NotNull(Validation.ValidateDiary(Diary(raw: new string('가', Validation.MaxContentLength + 1))));

    private static Schedule Sched(string title = "회의", string dt = "2026-08-24T10:00:00", string status = "confirmed") =>
        new() { Title = title, Datetime = dt, Status = status, Source = "tomorrow_plan" };

    [Fact]
    public void 올바른_일정은_통과한다() =>
        Assert.Null(Validation.ValidateSchedules([Sched()]));

    [Fact]
    public void 빈_제목_잘못된_시각_잘못된_상태는_거부한다()
    {
        Assert.NotNull(Validation.ValidateSchedules([Sched(title: " ")]));
        Assert.NotNull(Validation.ValidateSchedules([Sched(dt: "내일 10시")]));
        Assert.NotNull(Validation.ValidateSchedules([Sched(status: "maybe")]));
    }

    [Fact]
    public void 일괄_등록_상한을_넘으면_거부한다()
    {
        var many = Enumerable.Range(0, Validation.MaxSchedulesPerRequest + 1).Select(_ => Sched()).ToArray();
        Assert.NotNull(Validation.ValidateSchedules(many));
    }
}
