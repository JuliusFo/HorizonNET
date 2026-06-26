using HorizonNET.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    #region Creation

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Workspace>(e =>
        {
            e.HasKey(w => w.Id);
            e.Property(w => w.Name).IsRequired().HasMaxLength(200);
            e.Property(w => w.Description).HasMaxLength(1000);
        });

        modelBuilder.Entity<Project>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired().HasMaxLength(200);
            e.Property(p => p.Description).HasMaxLength(1000);
            e.Property(p => p.Status).HasConversion<string>();
            e.Property(p => p.Priority).HasConversion<string>();

            // Beim Löschen eines Arbeitsbereichs bleiben die Projekte erhalten
            // (Zuordnung wird auf null gesetzt).
            e.HasOne(p => p.Workspace)
                .WithMany(w => w.Projects)
                .HasForeignKey(p => p.WorkspaceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TaskItem>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Title).IsRequired().HasMaxLength(300);
            e.Property(t => t.Description).HasMaxLength(2000);
            e.Property(t => t.Priority).HasConversion<string>();
            e.Property(t => t.GoogleEventId).HasMaxLength(1024);

            e.HasOne(t => t.Project)
                .WithMany(p => p.Tasks)
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Sub-Tasks werden beim Löschen des Parent-Tasks mitgelöscht
            e.HasOne(t => t.ParentTask)
                .WithMany(t => t.SubTasks)
                .HasForeignKey(t => t.ParentTaskId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<GoogleConnection>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.RefreshToken).IsRequired();
            e.Property(c => c.Email).HasMaxLength(320);
        });
    }

    #endregion

    #region Entities

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<GoogleConnection> GoogleConnections => Set<GoogleConnection>();

    #endregion
}