using System.Text.Json;
using DailyMate.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var dbPath = Path.Combine(AppContext.BaseDirectory, "dailymate.db");
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseCors();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}

var json = DiaryRow.Json;

// ── Photos (F2-3 · F3-3) ─────────────────────────────────
var photosDir = Path.Combine(AppContext.BaseDirectory, "photos");
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
    var existing = await db.Diaries.FindAsync(entry.Date);
    var row = DiaryRow.FromDto(entry);
    if (existing is null) db.Diaries.Add(row);
    else db.Entry(existing).CurrentValues.SetValues(row);
    await db.SaveChangesAsync();
    return Results.Created($"/api/diaries/{entry.Date}", row.ToDto());
});

app.MapPut("/api/diaries/{date}", async (string date, DiaryEntry entry, AppDbContext db) =>
{
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

app.Run();
