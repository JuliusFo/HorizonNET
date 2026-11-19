using HorizonNET.Api.Services.Update;
using HorizonNET.Shared.Transfer.Update;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

// Auto-Update (ADR 0007): Stand anzeigen, prüfen, Installation anfordern. Hinter dem Login
// (Fallback-Policy). Installiert wird von der geplanten Aufgabe, nicht von diesem Prozess.
[ApiController]
[Route("api/update")]
public class UpdateController(UpdateCheckService checker, UpdateCoordinator coordinator) : ControllerBase
{
    [HttpGet]
    public ActionResult<UpdateInfoDto> Get() => coordinator.GetInfo();

    [HttpPost("check")]
    public async Task<ActionResult<UpdateInfoDto>> Check(CancellationToken ct)
    {
        await checker.CheckAsync(manual: true, ct);
        return coordinator.GetInfo();
    }

    [HttpPost("install")]
    public async Task<IActionResult> Install(CancellationToken ct)
    {
        var result = await coordinator.RequestInstallAsync(ct);
        if (result.Success) return Accepted(coordinator.GetInfo());
        return result.IsServerError
            ? Problem(result.Message, statusCode: StatusCodes.Status500InternalServerError)
            : Conflict(result.Message);
    }
}
