<#
.SYNOPSIS
  Compose reference boards from gldev_photo renders (BepInEx/devtools/photos/<prefab>/).
  Output: assets/reference/<prefab>/*.png (clean transparent views), <prefab>_board.png, lineup.png
.EXAMPLE
  ./scripts/make-boards.ps1                      # all four greydwarf-family creatures
  ./scripts/make-boards.ps1 -Creatures Greyling
#>
param(
    [string]$Photos = (Join-Path $env:APPDATA 'r2modmanPlus-local/Valheim/profiles/GreenlingsDev/BepInEx/devtools/photos'),
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) 'assets/reference'),
    [string[]]$Creatures = @()
)
$ErrorActionPreference = 'Stop'
$tool = Join-Path (Split-Path $PSScriptRoot -Parent) 'tools/BoardMaker'
dotnet build $tool -c Release -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'BoardMaker build failed' }
& dotnet (Join-Path $tool 'bin/Release/net8.0-windows/BoardMaker.dll') $Photos $Out @Creatures
if ($LASTEXITCODE -ne 0) { throw 'BoardMaker failed' }
