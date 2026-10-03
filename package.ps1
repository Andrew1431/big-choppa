# Builds Release and zips a Thunderstore package into dist\. Upload the zip at
# https://thunderstore.io/c/big-walk/create/
[CmdletBinding()]
param(
    [string]$GameDirectory = 'E:\SteamLibrary\steamapps\common\Big Walk',
    [string]$Team = 'Andrew1431'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$ts = Join-Path $root 'thunderstore'

$manifest = Get-Content -Raw (Join-Path $ts 'manifest.json') | ConvertFrom-Json
$version = $manifest.version_number
$pluginSource = Get-Content -Raw (Join-Path $root 'Plugin.cs')
if ($pluginSource -notmatch "PluginVersion = `"$([regex]::Escape($version))`"") {
    throw "manifest.json says $version but Plugin.cs PluginVersion differs. Bump both."
}
if (-not (Test-Path (Join-Path $ts 'icon.png'))) { & (Join-Path $ts 'make-icon.ps1') }

& (Join-Path $root 'build.ps1') -GameDirectory $GameDirectory -Publish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$dll = Join-Path $root 'bin\Release\net6.0\BigChoppa.dll'
# Belt and braces: dev-only code (DEVBUILD) must never ship.
if ([IO.File]::ReadAllText($dll).Contains('DevAutoHost')) { throw "BigChoppa.dll contains dev-only code; it was built without -Publish." }

$staging = Join-Path $root 'dist\staging'
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force $staging | Out-Null

foreach ($f in 'manifest.json', 'icon.png', 'README.md', 'CHANGELOG.md') {
    Copy-Item (Join-Path $ts $f) $staging
}
Copy-Item $dll $staging

$zip = Join-Path $root "dist\$Team-$($manifest.name)-$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item -Recurse -Force $staging
Write-Host "Package ready: $zip"
exit 0
