[CmdletBinding()]
param(
    [string]$GameDirectory = 'E:\SteamLibrary\steamapps\common\Big Walk',
    # BepInEx folder to build against and install into. Default: the game folder's, else r2modman's "Dev" profile.
    # Never the Default profile: that one runs the published Thunderstore version.
    [string]$BepInExDirectory,
    # Also copy the BepInEx loader itself (from this machine's install) to every remote target.
    [switch]$InstallBepInEx
)

$ErrorActionPreference = 'Stop'

$projectDirectory = $PSScriptRoot
if (-not $BepInExDirectory) {
    $candidates = @(
        (Join-Path $GameDirectory 'BepInEx'),
        (Join-Path $env:APPDATA 'r2modmanPlus-local\BigWalk\profiles\Dev\BepInEx')
    )
    $BepInExDirectory = $candidates | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'core\BepInEx.Unity.IL2CPP.dll') } | Select-Object -First 1
    if (-not $BepInExDirectory) { throw "BepInEx not found in: $($candidates -join ', '). Create an r2modman profile named 'Dev' and launch it once, or pass -BepInExDirectory." }
}
$coreDirectory = Join-Path $BepInExDirectory 'core'
$interopDirectory = Join-Path $BepInExDirectory 'interop'
$pluginDirectory = Join-Path $BepInExDirectory 'plugins\BigChoppa'
Write-Host "Using BepInEx at $BepInExDirectory"

if (-not (Test-Path -LiteralPath (Join-Path $coreDirectory 'BepInEx.Unity.IL2CPP.dll') -PathType Leaf)) {
    throw "BepInEx was not found at '$coreDirectory'."
}
# A Thunderstore copy installed by a mod manager would load alongside this dev build (same GUID, one gets skipped).
$installed = Get-ChildItem -LiteralPath (Join-Path $BepInExDirectory 'plugins') -Directory -Filter '*-BigChoppa' -ErrorAction SilentlyContinue
if ($installed) { Write-Warning "Mod-manager copy found at $($installed.FullName); disable it in r2modman or use a separate dev profile." }
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

# Extra machines to push to: one UNC path per line pointing at that machine's Big Walk game folder,
# e.g. \WIFE-PC\BigWalk. Lines starting with # are ignored. File is gitignored.
$targetsFile = Join-Path $projectDirectory 'remote-targets.txt'
if (Test-Path -LiteralPath $targetsFile) {
    foreach ($line in Get-Content -LiteralPath $targetsFile) {
        $target = $line.Trim()
        if (-not $target -or $target.StartsWith('#')) { continue }
        $dest = Join-Path $target 'BepInEx\plugins\BigChoppa'
        try {
            if ($InstallBepInEx) {
                foreach ($f in 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version') {
                    Copy-Item -LiteralPath (Join-Path $GameDirectory $f) -Destination $target -Force
                }
                robocopy (Join-Path $GameDirectory 'dotnet') (Join-Path $target 'dotnet') /E /NFL /NDL /NJH /NJS /NP | Out-Null
                # interop + cache are generated per game version; copying them skips the slow first launch.
                foreach ($d in 'core', 'patchers', 'unity-libs', 'interop', 'cache') {
                    $src = Join-Path $BepInExDirectory $d
                    if (Test-Path -LiteralPath $src) {
                        robocopy $src (Join-Path $target "BepInEx\$d") /E /NFL /NDL /NJH /NJS /NP | Out-Null
                    }
                }
                if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
                Write-Host "Installed BepInEx to $target"
            }
            New-Item -ItemType Directory -Force -Path $dest | Out-Null
            Copy-Item -LiteralPath $pluginPath -Destination $dest -Force
            $pdb = [IO.Path]::ChangeExtension($pluginPath, '.pdb')
            if (Test-Path -LiteralPath $pdb) { Copy-Item -LiteralPath $pdb -Destination $dest -Force }
            Write-Host "Pushed to $dest"
        }
        catch {
            Write-Warning "Could not push to $dest (game still running there, or share offline?): $($_.Exception.Message)"
        }
    }
}

# Ensure robocopy success codes (1-7) do not leak out as a failure exit code.
exit 0
