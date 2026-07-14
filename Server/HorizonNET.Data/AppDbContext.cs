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
            // Soft-Delete: gelöschte Zeilen global ausblenden.
            e.HasQueryFilter(w => w.DeletedAt == null);
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

            e.HasQueryFilter(p => p.DeletedAt == null);
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

            e.HasQueryFilter(t => t.DeletedAt == null);
        });

        modelBuilder.Entity<GoogleConnection>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.RefreshToken).IsRequired();
            e.Property(c => c.Email).HasMaxLength(320);
        });

        modelBuilder.Entity<Note>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Title).IsRequired().HasMaxLength(300);
            // Content ist HTML und kann lang werden – bewusst ohne MaxLength.

            // Beim Löschen eines Tasks bzw. Projekts bleibt die Notiz erhalten;
            // die jeweilige Zuordnung wird auf null gesetzt.
            e.HasOne(n => n.TaskItem)
                .WithMany()
                .HasForeignKey(n => n.TaskItemId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(n => n.Project)
                .WithMany()
                .HasForeignKey(n => n.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasQueryFilter(n => n.DeletedAt == null);
        });

        modelBuilder.Entity<DailyTask>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Title).IsRequired().HasMaxLength(300);
            // Bestehende Dailies bleiben "täglich" (127), wenn die Spalte hinzukommt.
            e.Property(t => t.WeekdayMask).HasDefaultValue((byte)127);

            // Projektzuordnung optional; beim Löschen des Projekts bleibt der Daily erhalten.
            e.HasOne(t => t.Project)
                .WithMany()
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasQueryFilter(t => t.DeletedAt == null);
        });

        modelBuilder.Entity<TaskTemplate>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Title).IsRequired().HasMaxLength(300);
            e.Property(t => t.Description).HasMaxLength(2000);
            e.Property(t => t.Priority).HasConversion<string>();

            // Projektzuordnung optional; beim Löschen des Projekts bleibt die Vorlage erhalten.
            e.HasOne(t => t.Project)
                .WithMany()
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasQueryFilter(t => t.DeletedAt == null);
        });

        modelBuilder.Entity<DailyTaskCompletion>(e =>
        {
            e.HasKey(c => c.Id);

            // Häkchen gehören zum Daily und werden mit ihm gelöscht.
            e.HasOne(c => c.DailyTask)
                .WithMany(t => t.Completions)
                .HasForeignKey(c => c.DailyTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            // Höchstens ein Häkchen pro Daily und Tag.
            e.HasIndex(c => new { c.DailyTaskId, c.Date }).IsUnique();

            // Passender Filter zum Soft-Delete des DailyTask (blendet Häkchen
            // gelöschter Dailies aus – vermeidet die EF-Filter-Warnung).
            e.HasQueryFilter(c => c.DailyTask!.DeletedAt == null);
        });
    }

    #endregion

    #region Entities

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<GoogleConnection> GoogleConnections => Set<GoogleConnection>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<DailyTask> DailyTasks => Set<DailyTask>();

    public DbSet<DailyTaskCompletion> DailyTaskCompletions => Set<DailyTaskCompletion>();

    public DbSet<TaskTemplate> TaskTemplates => Set<TaskTemplate>();

    #endregion
}