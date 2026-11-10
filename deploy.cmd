@echo off
rem Deploy Mediatheca to harbour: double-click, or run  deploy.cmd, optionally with --skip-tests or --rollback
rem The image is built ON harbour; no local Docker needed. Runs scripts\deploy-harbour.sh
rem in Git Bash (plain "bash" in PowerShell is WSL, which has no git).
setlocal
set "GITBASH=%ProgramFiles%\Git\bin\bash.exe"
if not exist "%GITBASH%" (
  echo Git Bash not found at "%GITBASH%".
  pause
  exit /b 1
)
"%GITBASH%" "%~dp0scripts\deploy-harbour.sh" %*
set "RC=%ERRORLEVEL%"
echo.
if "%RC%"=="0" (echo Deploy finished.) else (echo Deploy FAILED with exit code %RC%.)
pause
exit /b %RC%
