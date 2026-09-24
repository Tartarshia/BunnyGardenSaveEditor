# Optional local integration check. This reads the first installed UserData file
# through the editor's -SelfTest path and never writes a save.
& "$PSScriptRoot\BunnyGardenSaveEditor.ps1" -SelfTest
