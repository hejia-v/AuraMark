# AuraMark Acceptance Checklist

This checklist targets PRD section `6.3` and is designed for repeatable smoke acceptance.

## Quick Start

```powershell
cd C:\Dev\AuraMark
powershell -ExecutionPolicy Bypass -File .\scripts\acceptance.ps1 -Configuration Debug
```

Optional:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\acceptance.ps1 -Configuration Debug -OpenApp
```

## What the Script Does

1. Runs frontend build (`npm ci`, `npm run build`).
2. Runs solution build (`dotnet build AuraMark.sln`).
3. Verifies build artifacts:
   - `AuraMark.App.exe`
   - `EditorView/index.html`
   - `EditorView/assets`
4. Generates acceptance fixtures:
   - `large-6mb.md` (for loading test)
   - `image-case.md` + `sample.png` (for local image strategy)
   - `readonly.md` (for save error hint test)
5. Generates a run-specific manual checklist in `artifacts/acceptance-<timestamp>/manual-checklist.md`.

## PRD 6.3 Mapping

- Case 1: New -> type -> autosave -> restart
- Case 2: 5MB+ file loading and responsiveness
- Case 3: Immersive enter/exit interaction
- Case 4: External file change hot reload
- Case 5: Save failure soft hint + retry

## Cleanup

After running case 5, remove readonly flag:

```powershell
Set-ItemProperty -Path "<readonly.md full path>" -Name IsReadOnly -Value $false
```
