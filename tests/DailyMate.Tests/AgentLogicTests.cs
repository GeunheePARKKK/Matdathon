using DailyMate.Agent;

namespace DailyMate.Tests;

public class MockEngineTests
{
    [Fact]
    public void Detect_운동_회의_공부를_감지한다()
    {
        var spans = MockEngine.Detect("오늘은 아침에 헬스장에서 운동을 하고 출근했다. 점심에는 해커톤 기획 회의를 했다. 저녁에는 Azure 공부를 했다.");
        var types = spans.Select(s => s.Type).ToList();
        Assert.Contains("workout", types);
        Assert.Contains("meeting", types);
        Assert.Contains("study", types);
    }

    [Fact]
    public void Detect_오프셋은_원문과_일치한다()
    {
        var text = "아침에 러닝을 했다";
        var span = MockEngine.Detect(text).Single(s => s.Type == "workout");
        Assert.Equal("러닝", text[span.Start..span.End]);
    }

    [Fact]
    public void Detect_확장_주제_카페_게임을_감지한다()
    {
        var types = MockEngine.Detect("카페에서 친구를 만나고 게임을 했다").Select(s => s.Type);
        Assert.Contains("activity", types);
    }

    [Fact]
    public void Detect_겹치는_구간은_중복_감지하지_않는다()
    {
        var spans = MockEngine.Detect("헬스장에서 운동했다");
        Assert.All(spans, a => Assert.DoesNotContain(spans, b => b != a && a.Start < b.End && b.Start < a.End));
    }

    [Fact]
    public void ParseSchedules_확정과_미정을_구분한다()
    {
        var schedules = MockEngine.ParseSchedules("오전에 팀 스탠드업 있고, 저녁에 헬스장 갈까 생각중");
        Assert.Equal(2, schedules.Count);
        Assert.Equal("confirmed", schedules[0].Status);
        Assert.Equal("tentative", schedules[1].Status);
    }

    [Fact]
    public void ParseSchedules_시간대를_추론한다()
    {
        var schedules = MockEngine.ParseSchedules("오전에 회의 있어");
        Assert.Contains("T10:00", schedules[0].Datetime);
    }

    [Fact]
    public void Enrich_답변을_해당_문단에_굵게_삽입한다()
    {
        var raw = "헬스장에서 운동을 했다. 저녁에는 쉬었다.";
        var (enriched, hashtags) = MockEngine.Enrich(raw, ["workout"], ["벤치프레스 80kg 5x5"]);
        Assert.Contains("**벤치프레스 80kg 5x5.**", enriched);
        Assert.StartsWith("헬스장에서 운동을 했다.", enriched);
        Assert.Contains("#운동기록", hashtags);
    }

    [Fact]
    public void Enrich_스킵_답변은_삽입하지_않는다()
    {
        var raw = "헬스장에서 운동을 했다.";
        var (enriched, _) = MockEngine.Enrich(raw, ["workout"], ["패스"]);
        Assert.Equal(raw, enriched);
    }

    [Fact]
    public void ExtractMetadata_운동_중량과_세트를_추출한다()
    {
        var meta = MockEngine.ExtractMetadata(["workout"], ["스쿼트 100kg 5x5"], null);
        var workouts = (List<object>)meta["workouts"];
        Assert.Single(workouts);
    }
}

public class MockTriageTests
{
    [Fact]
    public void 루틴_요청을_감지한다()
    {
        Assert.True(MockTriage.IsRoutineRequest("가슴 루틴 짜줘"));
        Assert.True(MockTriage.IsRoutineRequest("내일 운동 뭐하지"));
        Assert.False(MockTriage.IsRoutineRequest("오늘 운동을 했다"));
    }

    [Fact]
    public void 통증과_피로를_구분한다()
    {
        var t = MockTriage.Analyze("어깨가 아파서 그런데 가슴 루틴 짜줘", []);
        Assert.Contains("shoulder_front", t.PainAreas!);
        Assert.Empty(t.FatiguedAreas!);

        var t2 = MockTriage.Analyze("어깨가 결리는데 가슴 루틴 짜줘", []);
        Assert.Contains("shoulder_front", t2.FatiguedAreas!);
        Assert.Empty(t2.PainAreas!);
    }

    [Fact]
    public void 대화_이력의_컨디션을_누적_반영한다()
    {
        var t = MockTriage.Analyze("등 루틴 짜줘", ["어제부터 허리가 아파"]);
        Assert.Contains("lower_back", t.PainAreas!);
        Assert.Equal("back", t.Target);
    }

    [Fact]
    public void 부위_미지정이면_target은_null이다()
    {
        var t = MockTriage.Analyze("운동 루틴 짜줘", []);
        Assert.Null(t.Target);
        Assert.True(t.IsWorkout);
    }

    [Fact]
    public void 머신_선호와_볼륨을_해석한다()
    {
        var t = MockTriage.Analyze("하체 루틴 짜줘 머신 위주로 짧게", []);
        Assert.Equal("machine", t.EquipmentPreference);
        Assert.Equal("short", t.VolumeRequest);
    }
}
