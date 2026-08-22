using DailyMate.McpTool;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace DailyMate.Tests;

public class FitnessToolsTests
{
    private sealed class TestEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static FitnessTools Create() => new(new ExerciseDb(new TestEnv()));

    [Fact]
    public void GetExercises_부위별_후보를_반환한다()
    {
        var result = Create().GetExercises("chest");
        Assert.NotEmpty(result.Exercises);
        Assert.Equal("chest", result.Target);
    }

    [Fact]
    public void CalcIntensity_고중량_비선호는_RPE_상한_7이다()
    {
        var r = Create().CalcIntensity("normal", [], heavyPreference: false);
        Assert.Equal(7, r.RpeCap);
    }

    [Fact]
    public void CalcIntensity_고피로는_볼륨을_줄인다()
    {
        var r = Create().CalcIntensity("high", [], heavyPreference: true);
        Assert.True(r.VolumeMultiplier < 1.0);
        Assert.True(r.ReduceAccessoryCount >= 1);
    }

    [Fact]
    public void BuildRoutine_통증_부위_운동은_전면_제외된다()
    {
        var tools = Create();
        var routine = tools.BuildRoutine(
            target: "chest", volumeMultiplier: 1.0, rpeCap: 8,
            painAreas: ["shoulder_front"]);
        // 어깨 관절 부하가 있는 운동이 루틴에 없어야 함
        var db = new ExerciseDb(new TestEnv());
        var shoulderLoaded = db.GetByTarget("chest")
            .Where(e => e.JointLoad.Contains("shoulder_front"))
            .Select(e => e.Name).ToHashSet();
        Assert.DoesNotContain(routine.Exercises, e => shoulderLoaded.Contains(e.Name) && e.IsWarmup != true);
    }

    [Fact]
    public void BuildRoutine_루틴은_계약_스키마를_충족한다()
    {
        var routine = Create().BuildRoutine("legs", 1.0, 8);
        Assert.Equal("fitness_routine", routine.Type);
        Assert.NotEmpty(routine.Exercises);
        Assert.All(routine.Exercises, e =>
        {
            Assert.True(e.Sets > 0);
            Assert.True(e.Reps > 0);
            Assert.InRange(e.Rpe, 1, 10);
        });
    }
}
