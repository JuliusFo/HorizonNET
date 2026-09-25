using HorizonNET.Api.Controllers;
using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Entities;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Server.Tests;

// Die Projekt-Zähler (Karte: "X% erledigt · Y offen") entstehen im Controller-Mapping.
// Was hier festgehalten wird: Sub-Tasks und Termine zählen NICHT mit – nur Haupt-Aufgaben,
// dieselbe Einheit, die die Projektliste als "offen" zeigt. Der Client spiegelt die
// Formel in ProjectDetail.SyncProjectCounts; weicht eine Seite ab, springt die Karte
// nach dem ersten Abhaken auf einen anderen Wert als nach dem Neuladen.
public class ProjectsControllerTests
{
    [Fact]
    public async Task GetById_CountsOnlyMainTasksThatAreNotAppointments()
    {
        using var db = new TestDatabase();
        int projectId;
        using (var seed = db.NewContext())
        {
            var project = new Project { Name = "Volleyball" };
            seed.Projects.Add(project);
            await seed.SaveChangesAsync();
            projectId = project.Id;

            var main = new TaskItem { Title = "Saison planen", ProjectId = projectId };
            seed.Tasks.AddRange(
                main,
                new TaskItem { Title = "Erledigt",   ProjectId = projectId, Status = WorkStatus.Done },
                new TaskItem { Title = "Training",   ProjectId = projectId, Kind = TaskKind.Appointment },
                new TaskItem { Title = "Spiel (war)", ProjectId = projectId, Kind = TaskKind.Appointment, Status = WorkStatus.Done });
            await seed.SaveChangesAsync();

            seed.Tasks.Add(new TaskItem { Title = "Sub", ProjectId = projectId, ParentTaskId = main.Id });
            await seed.SaveChangesAsync();
        }

        using var act = db.NewContext();
        var result = await new ProjectsController(new ProjectRepository(act)).GetById(projectId);

        var dto = Assert.IsType<ProjectResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, dto.TaskCount);     // zwei Haupt-Aufgaben; Sub-Task und beide Termine nicht
        Assert.Equal(1, dto.DoneTaskCount); // die erledigte Aufgabe; der vergangene Termin nicht
    }
}
