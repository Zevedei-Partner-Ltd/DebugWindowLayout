@echo off
setlocal
where msbuild >nul 2>nul
if errorlevel 1 (
  echo MSBuild was not found on PATH.
  echo Open a "Developer Command Prompt for VS 2026" and run build.cmd again.
  exit /b 1
)
msbuild DebugWindowLayout.sln /restore /t:Rebuild /p:Configuration=Release
if errorlevel 1 exit /b %errorlevel%
echo.
echo VSIX output should be under src\DebugWindowLayout\bin\Release\
