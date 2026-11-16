---
paths:
  - "scripts/**"
  - "**/*.ps1"
---

# PowerShell-Skripte

- **Als UTF-8 mit BOM speichern.** Windows PowerShell 5.1 auf dem vServer liest Dateien ohne BOM als ANSI; ein „–“ (UTF-8 `E2 80 93`) enthält dann das Byte `0x93`, ein typografisches Anführungszeichen, und zerreißt Strings – das ganze Skript ist voller Parse-Fehler (`server-install.ps1`, 2026-08-26). Das Write-Tool schreibt ohne BOM: danach umkodieren und mit `[System.Management.Automation.Language.Parser]::ParseFile` auf 0 Fehler prüfen.
- Die Skripte müssen unter Windows PowerShell 5.1 laufen (vServer), nicht nur unter PowerShell 7: keine Operatoren wie `??`, `?.` oder `&&`.
- `$ErrorActionPreference = 'Stop'` und nach nativen Aufrufen `$LASTEXITCODE` prüfen (Muster in `deploy-package.ps1`).
- Backups sichern DB **und** DataProtection-Schlüsselring zusammen (`backup-database.ps1`) – ohne Ring sind die verschlüsselten Inhalte verloren.
