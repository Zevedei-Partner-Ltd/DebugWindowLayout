$ErrorActionPreference = 'Stop'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw 'vswhere.exe not found. Install Visual Studio 2026 with the Visual Studio extension development workload.'
}

$installationPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if (-not $installationPath) {
    throw 'No Visual Studio installation with MSBuild was found.'
}

$msbuild = Join-Path $installationPath 'MSBuild\Current\Bin\MSBuild.exe'
if (-not (Test-Path $msbuild)) {
    throw "MSBuild not found at $msbuild"
}

& $msbuild "$PSScriptRoot\DebugWindowLayout.sln" /restore /t:Rebuild /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$vsix = Get-ChildItem "$PSScriptRoot\src\DebugWindowLayout\bin\Release" -Filter *.vsix -Recurse | Select-Object -First 1
if ($vsix) {
    Write-Host "VSIX: $($vsix.FullName)"
} else {
    Write-Warning 'Build succeeded, but no .vsix file was found in the expected output folder.'
}
