using Microsoft.EntityFrameworkCore;
using ScannerService.Domain.Entities;

namespace ScannerService.Infrastructure.Persistence;

public class Context : DbContext
{
    public Context(DbContextOptions<Context> options) : base(options) { }

    public DbSet<Profile> Profiles { get; set; }
    public DbSet<ExportSetting> ExportSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Profile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<ExportSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
        });
        // Initial Seed
        modelBuilder.Entity<ExportSetting>().HasData(new ExportSetting
        {
            Id = 1,
            Format = "PDF",
            ExportPath = "",
            FileName = "scan_{datetime}"
        });
    }
}
