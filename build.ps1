[CmdletBinding()]
param(
    [string]$GameDirectory = 'E:\SteamLibrary\steamapps\common\Big Walk'
)

$ErrorActionPreference = 'Stop'

$projectDirectory = $PSScriptRoot
$coreDirectory = Join-Path $GameDirectory 'BepInEx\core'
$interopDirectory = Join-Path $GameDirectory 'BepInEx\interop'
$pluginDirectory = Join-Path $GameDirectory 'BepInEx\plugins\BigChoppa'

if (-not (Test-Path -LiteralPath (Join-Path $coreDirectory 'BepInEx.Unity.IL2CPP.dll') -PathType Leaf)) {
    throw "BepInEx was not found at '$coreDirectory'. Install it into the game folder before building."
}
if (-not (Test-Path -LiteralPath (Join-Path $interopDirectory 'Assembly-CSharp.dll') -PathType Leaf)) {
    throw "Interop assemblies were not found at '$interopDirectory'. Launch the game once with BepInEx installed to generate them."
}

$arguments = @(
    'build',
    (Join-Path $projectDirectory 'BigChoppa.csproj'),
    '--configuration', 'Release',
    "--property:BepInExCoreDir=$coreDirectory",
    "--property:InteropDir=$interopDirectory",
    "--property:GamePluginDir=$pluginDirectory"
)

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$pluginPath = Join-Path $pluginDirectory 'BigChoppa.dll'
if (-not (Test-Path -LiteralPath $pluginPath -PathType Leaf)) {
    throw "The build succeeded but '$pluginPath' was not created."
}

Write-Host "Plugin installed: $pluginPath"
