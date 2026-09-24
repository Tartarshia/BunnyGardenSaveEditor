# BUNNY GARDEN Save Editor

Offline Windows save editor for the Steam version of BUNNY GARDEN.

It changes only a selected non-empty save slot's money and total affection for
Kana, Rin, and Miuka. It does not unlock routes, alter dates, modify daily
affection, or upload any data.

## Use

1. Exit BUNNY GARDEN completely.
2. Run `Launch-BunnyGardenSaveEditor.cmd`.
3. Select a non-empty slot, enter the requested values, then confirm.
4. Start the game and verify the changed values before continuing progress.

The editor discovers `UserData` beneath the standard Steam library path.
`Choose UserData...` supports other Steam libraries and Steam accounts.

Each write creates a timestamped `.bak` alongside the edited save, uses a
temporary file plus atomic replacement, then decodes the result to verify the
four changed values. When exact duplicate `UserData` mirrors exist, such as
with Steam Auto-Cloud, it can update those copies together. Non-identical files
are left untouched.

## Validation

Run the public, game-independent checks:

```powershell
.\test_public.ps1
```

With the game and a local save installed, this optional integration check
performs decode -> serialize -> compress -> decode entirely in memory and does
not modify a save file:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test_local_save.ps1
```

Create a redistributable ZIP with `./build.ps1 -Version 1.0.0`.

## Compatibility and safety

The current Steam/Windows format is
`Deflate(BinaryFormatter(GB.Save.SaveData))`. The editor intentionally runs in
Windows PowerShell 5.1, whose .NET Framework runtime contains the compatible
BinaryFormatter implementation.

Always retain the automatic backup and verify the result in game. Steam Cloud
conflicts and game-version changes remain outside the editor's control.
