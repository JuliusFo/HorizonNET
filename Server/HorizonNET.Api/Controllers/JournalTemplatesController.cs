using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

// Leitfragen-Vorlagen für Journal-Einträge. Bewusst unverschlüsselt: Eine Vorlage
// enthält die Fragen, nicht die Antworten.
[ApiController]
[Route("api/journaltemplates")]
public class JournalTemplatesController(IJournalTemplateRepository repo) : ControllerBase
{
    private static JournalTemplateResponseDto ToDto(JournalTemplate t) =>
        new(t.Id, t.Name, t.Content, t.SortOrder);

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok((await repo.GetAllAsync()).Select(ToDto));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var template = await repo.GetByIdAsync(id);
        return template is null ? NotFound() : Ok(ToDto(template));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JournalTemplateCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Name darf nicht leer sein.");

        var created = await repo.CreateAsync(new JournalTemplate
        {
            Name = dto.Name.Trim(),
            Content = dto.Content
        });

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] JournalTemplateUpdateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Name darf nicht leer sein.");

        var updated = await repo.UpdateAsync(id, new JournalTemplate
        {
            Name = dto.Name.Trim(),
            Content = dto.Content,
            SortOrder = dto.SortOrder
        });

        return updated is null ? NotFound() : Ok(ToDto(updated));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) =>
        await repo.DeleteAsync(id) ? NoContent() : NotFound();
}
