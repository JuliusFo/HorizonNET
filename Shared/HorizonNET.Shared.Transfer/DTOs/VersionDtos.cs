namespace HorizonNET.Shared.Transfer.DTOs;

// Version der laufenden API (Phase 9b). Version = InformationalVersion aus der zentralen
// Directory.Build.props, inkl. angehängtem "+{commit}" – so unterscheiden sich auch zwei
// Builds derselben Nummer, was die Versatz-Erkennung im Client (9c) nutzt.
// BuildUtc = Build-Zeitpunkt (Schreibzeit der Assembly), rein zur Anzeige.
public record AppVersionDto(string Version, DateTime? BuildUtc);
