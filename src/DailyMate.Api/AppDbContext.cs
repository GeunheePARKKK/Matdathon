using Microsoft.EntityFrameworkCore;

namespace DailyMate.Api;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<DiaryRow> Diaries => Set<DiaryRow>();
    public DbSet<Schedule> Schedules => Set<Schedule>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<DiaryRow>().HasKey(d => d.Date);
        mb.Entity<Schedule>().HasKey(s => s.Id);
    }
}
