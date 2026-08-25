namespace HorizonNET.Shared.Transfer.DTOs;

public record LoginRequestDto(string Username, string Password);

// Antwort von Login und /api/auth/me – bewusst nur der Name, mehr weiß die
// Einzelnutzer-App über ihren Benutzer nicht.
public record AuthUserDto(string Username);

public record ChangePasswordDto(string CurrentPassword, string NewPassword);
