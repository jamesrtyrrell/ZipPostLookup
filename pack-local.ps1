param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$out = Join-Path $PSScriptRoot "nupkg"

New-Item -ItemType Directory -Force -Path $out | Out-Null

dotnet pack ZipPostLookup/ZipPostLookup.csproj `
    --configuration Release `
    --output $out `
    /p:Version=$Version

dotnet pack ZipPostLookup.Legacy/ZipPostLookup.Legacy.csproj `
    --configuration Release `
    --output $out `
    /p:Version=$Version

Write-Host ""
Write-Host "Packages written to: $out" -ForegroundColor Cyan
Get-ChildItem $out -Filter "*.nupkg" | ForEach-Object {
    Write-Host "  $($_.Name)" -ForegroundColor Green
}
