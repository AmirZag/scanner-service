using Microsoft.EntityFrameworkCore;
using ScannerService.Domain.Entities;

namespace ScannerService.Infrastructure.Persistence;

public class ScannerDbContext : DbContext
{
    public ScannerDbContext(DbContextOptions<ScannerDbContext> options) : base(options) { }

    public DbSet<Profile> Profiles { get; set; }
    public DbSet<ExportSettings> ExportSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Profile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<ExportSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<ExportSettings>().HasData(new ExportSettings
        {
            Id = 1,
            OutputFormat = "PDF",
            OutputDirectory = "",
            FileName = "scan_{datetime}"
        });
    }
}