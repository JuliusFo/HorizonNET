using HorizonNET.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    #region Creation

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired().HasMaxLength(200);
            e.Property(p => p.Description).HasMaxLength(1000);
            e.Property(p => p.Status).HasConversion<string>();
            e.Property(p => p.Priority).HasConversion<string>();
        });

        modelBuilder.Entity<TaskItem>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Title).IsRequired().HasMaxLength(300);
            e.Property(t => t.Description).HasMaxLength(2000);
            e.Property(t => t.Priority).HasConversion<string>();

            e.HasOne(t => t.Project)
                .WithMany(p => p.Tasks)
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    #endregion

    #region Entities

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    #endregion
}