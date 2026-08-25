using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

// AllowAnonymous steht an den einzelnen Actions, nicht am Controller: Login muss ohne
// Sitzung erreichbar sein, Logout soll auch mit abgelaufener Sitzung nie fehlschlagen,
// und Me beantwortet die Frage "bin ich eingeloggt?" selbst mit 200 oder 401.
// ChangePassword dagegen verlangt die Sitzung – und ein AllowAnonymous am Controller
// ließe sich dort NICHT per [Authorize] zurücknehmen (AllowAnonymous gewinnt immer).
[ApiController]
[Route("api/auth")]
public class AuthController(SignInManager<IdentityUser> signInManager) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
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
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    // Für den Client: Ist die Cookie-Sitzung (noch) gültig, und wer bin ich?
    [HttpGet("me")]
    [AllowAnonymous]
    public IActionResult Me() =>
        User.Identity is { IsAuthenticated: true, Name: { } name }
            ? Ok(new AuthUserDto(name))
            : Unauthorized();

    // Verlangt die Sitzung (Fallback-Policy) UND das aktuelle Passwort – ein offener
    // Rechner allein reicht damit nicht, um den Login zu übernehmen.
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        if (string.IsNullOrEmpty(dto.CurrentPassword) || string.IsNullOrEmpty(dto.NewPassword))
            return BadRequest("Aktuelles und neues Passwort sind erforderlich.");

        var user = await signInManager.UserManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var result = await signInManager.UserManager
            .ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);

        if (!result.Succeeded)
            return BadRequest(string.Join(" ", result.Errors.Select(TranslateError)));

        // Der Passwortwechsel ändert den Security-Stamp; ohne frisches Cookie würde die
        // laufende Sitzung bei der nächsten Stamp-Prüfung kommentarlos abgemeldet.
        await signInManager.RefreshSignInAsync(user);
        return NoContent();
    }

    // Die Identity-Fehlertexte sind Englisch; die Codes sind stabil und dokumentiert.
    private static string TranslateError(IdentityError error) => error.Code switch
    {
        nameof(IdentityErrorDescriber.PasswordMismatch) =>
            "Das aktuelle Passwort ist falsch.",
        nameof(IdentityErrorDescriber.PasswordTooShort) =>
            "Das neue Passwort ist zu kurz (mindestens 6 Zeichen).",
        nameof(IdentityErrorDescriber.PasswordRequiresDigit) =>
            "Das neue Passwort braucht mindestens eine Ziffer.",
        nameof(IdentityErrorDescriber.PasswordRequiresUpper) =>
            "Das neue Passwort braucht mindestens einen Großbuchstaben.",
        nameof(IdentityErrorDescriber.PasswordRequiresLower) =>
            "Das neue Passwort braucht mindestens einen Kleinbuchstaben.",
        nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric) =>
            "Das neue Passwort braucht mindestens ein Sonderzeichen.",
        _ => error.Description
    };
}
