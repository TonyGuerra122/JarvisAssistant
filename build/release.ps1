<#
.SYNOPSIS
  Gera o instalador (Setup.exe) e os pacotes de atualização do J.A.R.V.I.S com o Velopack.

.EXAMPLE
  .\build\release.ps1 -Version 1.0.1                 # só empacota, em .\Releases
  $env:GITHUB_TOKEN = "ghp_..."; .\build\release.ps1 -Version 1.0.1 -Upload   # e publica no GitHub Releases

  Pré-requisito (uma vez): dotnet tool install -g vpk
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$Upload,
    [string]$Repo = "https://github.com/TonyGuerra122/JarvisAssistant",
    [string]$Channel = "win"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { throw "Versão inválida: use 1.2.3 (ou 1.2.3-beta.1)." }
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { throw "vpk não encontrado. Instale com: dotnet tool install -g vpk" }

$publish = Join-Path $root "publish"
$out = Join-Path $root "Releases"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host "==> Publicando $Version (self-contained, win-x64)" -ForegroundColor Cyan
dotnet publish GUI -c Release -r win-x64 --self-contained -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou." }

# Baixa as versões anteriores: é o que permite gerar atualizações incrementais (delta).
if ($Upload) {
    Write-Host "==> Baixando versões anteriores (para os deltas)" -ForegroundColor Cyan
    vpk download github --repoUrl $Repo --token $env:GITHUB_TOKEN --channel $Channel -o $out
    if ($LASTEXITCODE -ne 0) { Write-Warning "Sem versões anteriores (primeira publicação?). Seguindo." }
}

Write-Host "==> Empacotando" -ForegroundColor Cyan
vpk pack `
    --packId JarvisAssistantApp `
    --packVersion $Version `
    --packTitle "J.A.R.V.I.S" `
    --packAuthors "Tony Guerra" `
    --packDir $publish `
    --mainExe JarvisAssistant.exe `
    --icon (Join-Path $root "GUI\Assets\jarvis.ico") `
    --runtime win-x64 `
    --framework webview2 `
    --shortcuts Desktop,StartMenuRoot `
    --channel $Channel `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "vpk pack falhou." }

if ($Upload) {
    if (-not $env:GITHUB_TOKEN) { throw "Defina GITHUB_TOKEN para publicar." }
    Write-Host "==> Publicando no GitHub Releases" -ForegroundColor Cyan
    vpk upload github --repoUrl $Repo --token $env:GITHUB_TOKEN --publish --releaseName "J.A.R.V.I.S $Version" --tag "v$Version" --channel $Channel -o $out
    if ($LASTEXITCODE -ne 0) { throw "vpk upload falhou." }
}

Write-Host "Pronto. Instalador: $out\JarvisAssistantApp-$Channel-Setup.exe" -ForegroundColor Green
