$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\build.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$vsix = Get-ChildItem "$PSScriptRoot\src\DebugWindowLayout\bin\Release" -Filter *.vsix -Recurse | Select-Object -First 1
if (-not $vsix) { throw 'No VSIX found after build.' }
Start-Process $vsix.FullName
