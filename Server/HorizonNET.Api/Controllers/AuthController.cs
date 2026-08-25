using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(SignInManager<IdentityUser> signInManager) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest("Benutzername und Passwort sind erforderlich.");

        // lockoutOnFailure: Nach fünf Fehlversuchen sperrt Identity das Konto für einige
        // Minuten – die einzige Bremse gegen Durchprobieren, bis Rate-Limiting davorliegt.
        var result = await signInManager.PasswordSignInAsync(
            dto.Username, dto.Password, isPersistent: true, lockoutOnFailure: true);

        if (result.IsLockedOut)
            return StatusCode(StatusCodes.Status423Locked,
                "Zu viele Fehlversuche – das Konto ist vorübergehend gesperrt.");

        if (!result.Succeeded)
            return Unauthorized("Benutzername oder Passwort ist falsch.");

        return Ok(new AuthUserDto(dto.Username));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    // Für den Client: Ist die Cookie-Sitzung (noch) gültig, und wer bin ich?
    [HttpGet("me")]
    public IActionResult Me() =>
        User.Identity is { IsAuthenticated: true, Name: { } name }
            ? Ok(new AuthUserDto(name))
            : Unauthorized();
}
