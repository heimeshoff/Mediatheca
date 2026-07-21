# Publish the Photino desktop shell as a self-contained Windows app.
# No .NET runtime install required on the target machine.
#
# Run from the repo root. Build the client first (`npm run build`) so
# deploy/public exists — Desktop.fsproj copies it into the publish output.
#
# Usage: powershell -File scripts/publish-desktop-win.ps1 [-OutDir <path>]

param(
    [string]$OutDir = "publish/desktop-win-x64"
)

dotnet publish src/Desktop/Desktop.fsproj `
    -c Release `
    -r win-x64 `
    --self-contained `
    -o $OutDir

Write-Host "Published to $OutDir — run $OutDir/Desktop.exe"
