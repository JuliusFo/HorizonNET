using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

// Tagebuch. Der Schlüssel ist durchgehend das Datum, nicht die Id – deshalb liegen
// Lesen und Schreiben eines Tages auf /api/journal/{date} statt auf einer Id-Route.
// Journal-Inhalte sind verschlüsselt gespeichert (siehe EncryptedConverter); dieser
// Controller sieht sie im Klartext, weil die Entschlüsselung in EF passiert.
[ApiController]
[Route("api/[controller]")]
public class JournalController(
    IJournalRepository repo,
    ITaskRepository tasks,
    IDailyTaskRepository dailies,
    ITimeEntryRepository times,
    IExerciseSetRepository sets,
    IBodyWeightRepository weights) : ControllerBase
{
    private static MoodResponseDto ToDto(MoodEntry m) =>
        new(m.Id, m.RecordedAt, m.Mood, m.Energy, m.Note);

    private static JournalEntryResponseDto ToDto(JournalEntry j) =>
        new(j.Id, j.Date, j.Title, j.Content, j.Tags,
            j.ProjectId, j.Project?.Name, j.TaskItemId, j.TaskItem?.Title,
            j.CreatedAt, j.UpdatedAt,
            j.Moods.OrderBy(m => m.RecordedAt).Select(ToDto).ToList());

    // Ohne Content: Liste, Heatmap und Kurve zeigen ihn nicht, und er ist der mit
    // Abstand größte Teil eines Eintrags.
    private static JournalListItemDto ToListItem(JournalEntry j)
    {
        var moods = j.Moods.ToList();
        return new JournalListItemDto(
            j.Id, j.Date, j.Title,
            !string.IsNullOrWhiteSpace(j.Content),
            j.Tags,
            moods.Count,
            moods.Count == 0 ? null : moods.Min(m => m.Mood),
            moods.Count == 0 ? null : moods.Max(m => m.Mood),
            moods.Count == 0 ? null : moods.Average(m => (double)m.Mood),
            j.UpdatedAt);
    }

    // ── Tageseintrag ─────────────────────────────────────────────────────────────

    // 404, wenn für den Tag noch nichts existiert. Das ist der Normalfall beim
    // Blättern und ausdrücklich kein Fehler – der Client behandelt es lokal
    // (siehe ApiErrorHandler: 404 bei GET löst bewusst keinen Toast aus).
    [HttpGet("{date:datetime}")]
    public async Task<IActionResult> GetByDate(DateTime date)
    {
        var entry = await repo.GetByDateAsync(DateOnly.FromDateTime(date));
        return entry is null ? NotFound() : Ok(ToDto(entry));
    }

    [HttpGet]
    public async Task<IActionResult> GetRange([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var entries = await repo.GetRangeAsync(from, to);
        return Ok(entries.Select(ToListItem));
    }

    // Anlegen UND Ändern: Der Tag ist der Schlüssel, ein zweiter Aufruf aktualisiert
    // (gleiches Muster wie beim Körpergewicht).
    [HttpPut("{date:datetime}")]
    public async Task<IActionResult> Upsert(DateTime date, [FromBody] JournalEntryUpsertDto dto)
    {
        var entry = await repo.UpsertAsync(new JournalEntry
        {
            Date = DateOnly.FromDateTime(date),
            Title = dto.Title,
            Content = dto.Content,
            Tags = NormalizeTags(dto.Tags),
            ProjectId = dto.ProjectId,
            TaskItemId = dto.TaskItemId
        });

        return Ok(ToDto(entry));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) =>
        await repo.DeleteAsync(id) ? NoContent() : NotFound();

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id) =>
        await repo.RestoreAsync(id) ? NoContent() : NotFound();

    // Eigene Papierkorb-Ansicht statt des globalen Papierkorbs – Journal-Inhalte
    // haben dort nichts zu suchen.
    [HttpGet("deleted")]
    public async Task<IActionResult> GetDeleted()
    {
        var entries = await repo.GetDeletedAsync();
        return Ok(entries.Select(j =>
            new JournalDeletedItemDto(j.Id, j.Date, j.Title, j.DeletedAt!.Value)));
    }

    [HttpDelete("{id:int}/purge")]
    public async Task<IActionResult> Purge(int id) =>
        await repo.PurgeAsync(id) ? NoContent() : NotFound();

    // ── Suche ────────────────────────────────────────────────────────────────────

    // Sucht in Tagestext, Überschrift und Stimmungsnotizen. Der Weg ist zweistufig,
    // weil die Textspalten verschlüsselt sind – siehe JournalRepository.SearchAsync.
    //
    // Bewusst ein eigener Endpunkt und NICHT Teil der globalen Palette (Strg+K): Wer
    // nach "Urlaub" sucht, soll nicht ungefragt Tagebucheinträge in einer Liste neben
    // Tasks und Notizen stehen haben.
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q, [FromQuery] string? tag,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(tag))
            return Ok(Array.Empty<JournalSearchHitDto>());

        var hits = await repo.SearchAsync(q, tag, from, to, Math.Clamp(limit, 1, 200));

        return Ok(hits.Select(j => new JournalSearchHitDto(
            j.Date,
            j.Title,
            NoteSnippet.From(j.Content),
            j.Tags,
            j.Moods.Count == 0 ? null : j.Moods.Average(m => (double)m.Mood))));
    }

    // Stichworte mit Häufigkeit, häufigste zuerst – trägt die Tag-Wolke als Filter.
    [HttpGet("tags")]
    public async Task<IActionResult> GetTags()
    {
        var raw = await repo.GetAllTagsAsync();

        var counted = raw
            .SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .GroupBy(t => t)
            .Select(g => new JournalTagDto(g.Key, g.Count()))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Tag)
            .ToList();

        return Ok(counted);
    }

    // ── „An diesem Tag" ──────────────────────────────────────────────────────────

    // Zurückliegende Einträge zum selben Kalendertag. Die Antwort trägt bewusst KEINEN
    // Text: Sie wird auf der Heute-Seite gezeigt, also außerhalb der Journal-Sperre.
    // Gelesen wird über den Link ins Journal – und damit hinter der Sperre.
    [HttpGet("onthisday")]
    public async Task<IActionResult> GetOnThisDay([FromQuery] DateOnly? date)
    {
        var today = date ?? DateOnly.FromDateTime(DateTime.Now);

        // Ein Monat plus die Jahrestage. Weiter zurück als fünf Jahre zu schauen lohnt
        // nicht – so lange gibt es die App nicht, und leere Abfragen kosten trotzdem.
        var candidates = new List<DateOnly> { today.AddMonths(-1) };
        for (var years = 1; years <= 5; years++) candidates.Add(today.AddYears(-years));

        var hits = new List<OnThisDayDto>();
        foreach (var day in candidates)
        {
            var entry = await repo.GetByDateAsync(day);
            if (entry is null) continue;

            var hasContent = !string.IsNullOrWhiteSpace(entry.Content);
            var moods = entry.Moods.ToList();
            if (!hasContent && moods.Count == 0) continue;

            hits.Add(new OnThisDayDto(
                day, hasContent, moods.Count,
                moods.Count == 0 ? null : moods.Average(m => (double)m.Mood)));
        }

        return Ok(hits);
    }

    // ── Tagesrückblick ───────────────────────────────────────────────────────────

    // Bündelt, was die App über den Tag ohnehin weiß. Bewusst ein eigener Endpunkt und
    // nicht Teil des Eintrags: Der Eintrag wird gespeichert, dieser Kontext nie – er
    // wird bei jedem Öffnen frisch gelesen.
    [HttpGet("{date:datetime}/context")]
    public async Task<IActionResult> GetContext(DateTime date)
    {
        var day = DateOnly.FromDateTime(date);
        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var now = DateTime.Now;

        var completed = (await tasks.GetCompletedOnAsync(day))
            .Select(t => new ContextTaskDto(t.Id, t.Title, t.Project?.Name))
            .ToList();

        // Geplant heißt: aktiv und für diesen Wochentag vorgesehen (Bitmaske).
        // Ungenauigkeit bei Altdaten: Ein erst später angelegter Daily zählt auch für
        // frühere Tage als geplant – DailyTask trägt kein Anlagedatum.
        var active = await dailies.GetActiveAsync();
        var planned = active
            .Where(d => (d.WeekdayMask & (1 << (int)day.DayOfWeek)) != 0)
            .ToList();
        var done = planned.Count(d => d.Completions.Any(c => c.Date == day));

        // Anteil je Intervall, das den Tag berührt – ein über Mitternacht laufender
        // Timer zählt nur mit dem Teil, der wirklich an diesem Tag lag.
        var perTask = (await times.GetForDayAsync(day))
            .Select(t =>
            {
                var from = t.StartedAt > dayStart ? t.StartedAt : dayStart;
                var until = (t.EndedAt ?? now) < dayEnd ? (t.EndedAt ?? now) : dayEnd;

                return new
                {
                    t.TaskItemId,
                    Title = t.TaskItem?.Title ?? "(ohne Titel)",
                    Minutes = (int)Math.Round((until - from).TotalMinutes)
                };
            })
            .Where(t => t.Minutes > 0)
            .GroupBy(t => new { t.TaskItemId, t.Title })
            .Select(g => new ContextTimeDto(g.Key.TaskItemId, g.Key.Title, g.Sum(x => x.Minutes)))
            .OrderByDescending(t => t.Minutes)
            .ToList();

        var sport = (await sets.GetAsync(dayStart, dayEnd, null))
            .GroupBy(s => s.Exercise!)
            .Select(g => new ContextSportDto(g.Key.Name, SummarizeSport(g.Key.Kind, g.ToList())))
            .ToList();

        var weight = (await weights.GetAsync(day, day)).FirstOrDefault()?.WeightKg;

        return Ok(new JournalContextDto(
            completed, done, planned.Count,
            perTask.Sum(t => t.Minutes), perTask, sport, weight));
    }

    // Welche Kennzahl zählt, hängt vom Übungstyp ab – dieselbe Fallunterscheidung wie
    // in der Sport-Auswertung. Hier bewusst serverseitig formuliert, damit der Client
    // die Sportlogik nicht ein zweites Mal kennen muss.
    private static string SummarizeSport(ExerciseKind kind, List<ExerciseSet> sets)
    {
        switch (kind)
        {
            case ExerciseKind.Endurance:
                var meters = sets.Sum(s => s.DistanceMeters ?? 0);
                var seconds = sets.Sum(s => s.DurationSeconds ?? 0);
                var parts = new List<string>();
                if (meters > 0) parts.Add($"{meters / 1000.0:0.0} km");
                if (seconds > 0) parts.Add($"{seconds / 60}:{seconds % 60:00} min");
                return parts.Count > 0
                    ? string.Join(" · ", parts)
                    : sets.Count == 1 ? "1 Einheit" : $"{sets.Count} Einheiten";

            case ExerciseKind.Bodyweight:
                return $"{Sets(sets.Count)} · {sets.Sum(s => s.Reps ?? 0)} Wdh.";

            default:
                var volume = sets.Sum(s => (s.Reps ?? 0) * (s.WeightKg ?? 0));
                return volume > 0
                    ? $"{Sets(sets.Count)} · {volume:N0} kg"
                    : $"{Sets(sets.Count)} · {sets.Sum(s => s.Reps ?? 0)} Wdh.";
        }
    }

    // ── Stimmungen ───────────────────────────────────────────────────────────────

    [HttpPost("{date:datetime}/moods")]
    public async Task<IActionResult> AddMood(DateTime date, [FromBody] MoodCreateDto dto)
    {
        if (Validate(dto.Mood, dto.Energy) is { } error) return BadRequest(error);

        var day = DateOnly.FromDateTime(date);

        var mood = await repo.AddMoodAsync(day, new MoodEntry
        {
            Mood = dto.Mood,
            Energy = dto.Energy,
            Note = dto.Note,
            RecordedAt = dto.RecordedAt ?? DefaultRecordedAt(day)
        });

        return Ok(ToDto(mood));
    }

    [HttpPut("moods/{id:int}")]
    public async Task<IActionResult> UpdateMood(int id, [FromBody] MoodUpdateDto dto)
    {
        if (Validate(dto.Mood, dto.Energy) is { } error) return BadRequest(error);

        var mood = await repo.UpdateMoodAsync(id, new MoodEntry
        {
            Mood = dto.Mood,
            Energy = dto.Energy,
            Note = dto.Note,
            RecordedAt = dto.RecordedAt
        });

        return mood is null ? NotFound() : Ok(ToDto(mood));
    }

    [HttpDelete("moods/{id:int}")]
    public async Task<IActionResult> DeleteMood(int id) =>
        await repo.DeleteMoodAsync(id) ? NoContent() : NotFound();

    // Ohne angegebene Uhrzeit: Am heutigen Tag ist "jetzt" richtig – das ist der
    // Normalfall (ein Klick aufs Emoji). Trägt man dagegen einen vergangenen Tag nach,
    // wäre "jetzt" schlicht falsch: Der Punkt landete am Ende des Zeitstrahls, obwohl
    // niemand weiß, wann an jenem Tag die Stimmung galt. Dann Tagesmitte als neutrale
    // Annahme – sichtbar in der Mitte und bewusst zum Korrigieren einladend.
    private static DateTime DefaultRecordedAt(DateOnly day) =>
        day == DateOnly.FromDateTime(DateTime.Now)
            ? DateTime.Now
            : day.ToDateTime(new TimeOnly(12, 0));

    private static string Sets(int count) => count == 1 ? "1 Satz" : $"{count} Sätze";

    // Stimmung 1..5, Energie 0..10. Energie 0 ist gültig ("komplett leer") und
    // bedeutet etwas anderes als null (nicht erfasst).
    private static string? Validate(byte mood, byte? energy)
    {
        if (mood is < 1 or > 5) return "Stimmung muss zwischen 1 und 5 liegen.";
        if (energy is > 10) return "Energie muss zwischen 0 und 10 liegen.";
        return null;
    }

    // Stichworte vereinheitlichen: klein, ohne Leerraum, ohne Dubletten und Leereinträge.
    // Sonst stehen "Sport", "sport " und "sport" als drei Tags in der Wolke.
    private static string? NormalizeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return null;

        var cleaned = tags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Distinct()
            .ToList();

        return cleaned.Count == 0 ? null : string.Join(',', cleaned);
    }
}
