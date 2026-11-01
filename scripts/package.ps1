<#
.SYNOPSIS
  Build a Release and package it as a Thunderstore / r2modman zip in dist/.
  In r2modman: Settings > Import local mod > pick the zip.
#>
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent

$version = ([xml](Get-Content "$Root\src\Greenlings\Greenlings.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$pluginVersion = (Select-String -Path "$Root\src\Greenlings\Plugin.cs" -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
if ($version -ne $pluginVersion) { throw "Version mismatch: csproj $version vs Plugin.cs $pluginVersion" }

dotnet build "$Root\src\Greenlings\Greenlings.csproj" -c Release -p:DeployToProfile=false -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

$stage = Join-Path $Root "dist\stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force "$stage\plugins\Greenlings" | Out-Null

Copy-Item "$Root\src\Greenlings\bin\Release\Greenlings.dll" "$stage\plugins\Greenlings\"
Copy-Item "$Root\assets\icon.png" $stage
Copy-Item "$Root\assets\README.md" $stage
@{
    name           = 'Greenlings'
    version_number = $version
    website_url    = ''
    description    = 'Tame greylings with coins and teach them chores by showing them: hauling, smelting, cooking, harvesting.'
    dependencies   = @('denikson-BepInExPack_Valheim-5.4.2351')
} | ConvertTo-Json | Set-Content "$stage\manifest.json" -Encoding utf8NoBOM

$zip = Join-Path $Root "dist\Greenlings-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Remove-Item -Recurse -Force $stage
Write-Host "packaged $zip"
