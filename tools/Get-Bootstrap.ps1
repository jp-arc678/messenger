<#
    ดาวน์โหลด Bootstrap 5 มาเก็บไว้ในโปรเจกต์แบบ local

    ระบบนี้ใช้งานภายในองค์กร (intranet) จึงไม่พึ่ง CDN
    รันสคริปต์นี้ครั้งเดียวหลัง clone โปรเจกต์

    ใช้:  pwsh -File tools\Get-Bootstrap.ps1
#>

[CmdletBinding()]
param(
    [string] $Version = '5.3.3'
)

$ErrorActionPreference = 'Stop'

$root      = Split-Path -Parent $PSScriptRoot
$contentIn = Join-Path $root 'src\Web\Content'
$scriptIn  = Join-Path $root 'src\Web\Scripts'

New-Item -ItemType Directory -Force -Path $contentIn | Out-Null
New-Item -ItemType Directory -Force -Path $scriptIn  | Out-Null

$base = "https://cdn.jsdelivr.net/npm/bootstrap@$Version/dist"

$downloads = @(
    @{ Url = "$base/css/bootstrap.min.css";        Path = Join-Path $contentIn 'bootstrap.min.css' },
    @{ Url = "$base/js/bootstrap.bundle.min.js";   Path = Join-Path $scriptIn  'bootstrap.bundle.min.js' }
)

foreach ($d in $downloads) {
    Write-Host "ดาวน์โหลด $($d.Url)"
    Invoke-WebRequest -Uri $d.Url -OutFile $d.Path -UseBasicParsing
    $size = [math]::Round((Get-Item $d.Path).Length / 1KB, 1)
    Write-Host "  -> $($d.Path) ($size KB)"
}

Write-Host "เสร็จแล้ว — Bootstrap $Version" -ForegroundColor Green
