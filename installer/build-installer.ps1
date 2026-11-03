$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "publish"
$issPath = Join-Path $PSScriptRoot "GasAgencyManager.iss"

Write-Host "Publishing app to: $publishDir"
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

dotnet publish (Join-Path $repoRoot "GasAgencyManager.csproj") `
  -c Release `
  -r win-x64 `
  -o $publishDir `
  -p:PublishSingleFile=true `
  -p:SelfContained=true `
  -p:IncludeNativeLibrariesForSelfExtract=true | Out-Host

$candidateIscc = @(
  (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
  (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)

$iscc = $candidateIscc | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) {
  throw "Inno Setup not found. Install Inno Setup 6, then re-run. Expected ISCC.exe in: `n- $($candidateIscc -join "`n- ")"
}

Write-Host "Compiling installer via: $iscc"
& $iscc $issPath | Out-Host

Write-Host "Done. Check: $(Join-Path $PSScriptRoot "dist")"



