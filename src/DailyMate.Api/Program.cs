using System.Text.Json;
using DailyMate.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// 데이터 디렉토리: 쓰기 가능한 첫 경로 사용 (컨테이너 /app은 읽기 전용일 수 있음)
static string PickDataDir()
{
    var candidates = new[]
    {
        Environment.GetEnvironmentVariable("DAILYMATE_DATA"),
        AppContext.BaseDirectory,
        Path.GetTempPath(),
    };
    foreach (var dir in candidates)
    {
        if (string.IsNullOrEmpty(dir)) continue;
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return dir;
        }
        catch { /* 다음 후보 */ }
    }
    return Path.GetTempPath();
}

var dataDir = PickDataDir();
var dbPath = Path.Combine(dataDir, "dailymate.db");
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
// Aspire 서비스 디스커버리로 agent 프록시용 클라이언트 구성
builder.Services.AddHttpClient("agent", c => { c.BaseAddress = new Uri("http://agent"); c.Timeout = TimeSpan.FromMinutes(3); });

// 남용 방지: IP당 고정 윈도 레이트 리밋 (읽기 120/분, 쓰기·에이전트 30/분)
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var writeOrAgent = ctx.Request.Method != "GET" || ctx.Request.Path.StartsWithSegments("/agent");
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            $"{ip}:{(writeOrAgent ? "w" : "r")}",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = writeOrAgent ? 30 : 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });
});
// 요청 바디 크기 제한 (사진 업로드 최대 12MB)
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 12 * 1024 * 1024);

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseCors();
app.UseRateLimiter();

// 보안 헤더
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; font-src 'self' https://cdn.jsdelivr.net https://fonts.gstatic.com; script-src 'self'; connect-src 'self'";
    await next();
});

// 배포 모드: 빌드된 React 정적 파일 서빙 (같은 오리진 → CORS·프록시 불필요)
var hasWebRoot = File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html"));
if (hasWebRoot)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// /agent/* → agent 서비스 프록시 (SSE 스트리밍 지원)
app.Map("/agent/{**path}", async (HttpContext ctx, IHttpClientFactory factory) =>
{
    var client = factory.CreateClient("agent");
    using var req = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), $"{ctx.Request.Path}{ctx.Request.QueryString}");
    if (ctx.Request.ContentLength > 0 || ctx.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
        req.Content = new StreamContent(ctx.Request.Body);
        if (ctx.Request.ContentType is { } ct) req.Content.Headers.TryAddWithoutValidation("Content-Type", ct);
    }
    using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
    ctx.Response.StatusCode = (int)res.StatusCode;
    if (res.Content.Headers.ContentType is { } rct) ctx.Response.ContentType = rct.ToString();
    await res.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
});

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}

var json = DiaryRow.Json;

// ── Photos (F2-3 · F3-3) ─────────────────────────────────
var photosDir = Path.Combine(dataDir, "photos");
Directory.CreateDirectory(photosDir);
string[] allowedExt = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

app.MapPost("/api/photos", async (HttpRequest req) =>
{
    var form = await req.ReadFormAsync();
    var linkedTopic = form["linkedTopic"].FirstOrDefault();
    var results = new List<Photo>();
    foreach (var file in form.Files)
    {
        if (file.Length is 0 or > 10 * 1024 * 1024) continue;
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExt.Contains(ext)) continue;
        var id = Guid.NewGuid().ToString("N");
        var name = id + ext;
        await using var fs = File.Create(Path.Combine(photosDir, name));
        await file.CopyToAsync(fs);
        results.Add(new Photo(id, $"/photos/{name}", file.FileName, string.IsNullOrEmpty(linkedTopic) ? null : linkedTopic));
    }
    return results.Count > 0 ? Results.Created("/api/photos", results) : Results.BadRequest(new { message = "지원하는 이미지(jpg/png/gif/webp, 10MB 이하)가 없어요." });
});

app.MapGet("/photos/{name}", (string name) =>
{
    if (name.Contains("..") || name.Contains('/')) return Results.BadRequest();
    var path = Path.Combine(photosDir, name);
    if (!File.Exists(path)) return Results.NotFound();
    var mime = Path.GetExtension(name) switch
    {
        ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp", _ => "image/jpeg"
    };
    return Results.File(path, mime);
});

// ── Diaries ──────────────────────────────────────────────
app.MapGet("/api/diaries", async (AppDbContext db) =>
    (await db.Diaries.OrderByDescending(d => d.Date).ToListAsync()).Select(d => d.ToDto()));

app.MapGet("/api/diaries/{date}", async (string date, AppDbContext db) =>
    await db.Diaries.FindAsync(date) is { } row ? Results.Ok(row.ToDto()) : Results.NotFound());

app.MapPost("/api/diaries", async (DiaryEntry entry, AppDbContext db) =>
{
    if (Validation.ValidateDiary(entry) is { } error)
        return Results.BadRequest(new { message = error });
    var existing = await db.Diaries.FindAsync(entry.Date);
    var row = DiaryRow.FromDto(entry);
    if (existing is null) db.Diaries.Add(row);
    else db.Entry(existing).CurrentValues.SetValues(row);
    await db.SaveChangesAsync();
    return Results.Created($"/api/diaries/{entry.Date}", row.ToDto());
});

app.MapPut("/api/diaries/{date}", async (string date, DiaryEntry entry, AppDbContext db) =>
{
    if (Validation.ValidateDiary(entry with { Date = date }) is { } error)
        return Results.BadRequest(new { message = error });
    var existing = await db.Diaries.FindAsync(date);
    if (existing is null) return Results.NotFound();
    db.Entry(existing).CurrentValues.SetValues(DiaryRow.FromDto(entry with { Date = date }));
    await db.SaveChangesAsync();
    return Results.Ok(existing.ToDto());
});

app.MapDelete("/api/diaries/{date}", async (string date, AppDbContext db) =>
{
    var row = await db.Diaries.FindAsync(date);
    if (row is null) return Results.NotFound();
    db.Diaries.Remove(row);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ── Schedules ────────────────────────────────────────────
app.MapGet("/api/schedules", async (string? date, AppDbContext db) =>
{
    var q = db.Schedules.AsQueryable();
    if (!string.IsNullOrEmpty(date)) q = q.Where(s => s.Datetime.StartsWith(date));
    return await q.OrderBy(s => s.Datetime).ToListAsync();
});

app.MapPost("/api/schedules", async (Schedule[] schedules, AppDbContext db) =>
{
    if (Validation.ValidateSchedules(schedules) is { } error)
        return Results.BadRequest(new { message = error });
    foreach (var s in schedules)
    {
        if (string.IsNullOrEmpty(s.Id)) s.Id = Guid.NewGuid().ToString("N");
        db.Schedules.Add(s);
    }
    await db.SaveChangesAsync();
    return Results.Created("/api/schedules", schedules);
});

app.MapPatch("/api/schedules/{id}", async (string id, JsonElement body, AppDbContext db) =>
{
    var s = await db.Schedules.FindAsync(id);
    if (s is null) return Results.NotFound();
    if (body.TryGetProperty("done", out var done)) s.Done = done.GetBoolean();
    await db.SaveChangesAsync();
    return Results.Ok(s);
});

app.MapDelete("/api/schedules/{id}", async (string id, AppDbContext db) =>
{
    var s = await db.Schedules.FindAsync(id);
    if (s is null) return Results.NotFound();
    db.Schedules.Remove(s);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ── Stats ────────────────────────────────────────────────
app.MapGet("/api/stats/weekly", async (AppDbContext db) =>
{
    var weekAgo = DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd");
    var diaries = await db.Diaries.Where(d => d.Date.CompareTo(weekAgo) >= 0).ToListAsync();
    var meetings = diaries.Sum(d =>
    {
        try
        {
            var meta = JsonSerializer.Deserialize<JsonElement>(d.MetadataJson);
            return meta.TryGetProperty("meetings", out var m) ? m.GetArrayLength() : 0;
        }
        catch { return 0; }
    });
    var schedules = await db.Schedules.CountAsync(s => s.Datetime.CompareTo(weekAgo) >= 0);
    return new { diaryDays = diaries.Count, meetings, schedules };
});

// ── Export (Markdown / JSON) ─────────────────────────────
app.MapPost("/api/export", async (JsonElement body, AppDbContext db) =>
{
    var date = body.GetProperty("date").GetString()!;
    var format = body.TryGetProperty("format", out var f) ? f.GetString() : "markdown";
    var row = await db.Diaries.FindAsync(date);
    if (row is null) return Results.NotFound();
    if (format == "json")
        return Results.File(JsonSerializer.SerializeToUtf8Bytes(row.ToDto(), json), "application/json", $"diary-{date}.json");
    var md = $"# {date} 일기\n\n{(string.IsNullOrEmpty(row.EnrichedContent) ? row.RawContent : row.EnrichedContent)}\n";
    return Results.File(System.Text.Encoding.UTF8.GetBytes(md), "text/markdown", $"diary-{date}.md");
});

// SPA 라우팅 폴백 (배포 모드)
if (hasWebRoot) app.MapFallbackToFile("index.html");

app.Run();
