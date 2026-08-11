<#
    ตรวจสอบโปรเจกต์แบบครบวงจร : รัน SQL scripts -> build -> unit test

    ใช้:  pwsh -File tools\Verify.ps1
    ทุกสคริปต์ SQL รันซ้ำได้ จึงเรียกสคริปต์นี้กี่ครั้งก็ได้
#>

[CmdletBinding()]
param(
    [string] $SqlServer = 'localhost'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'

if (-not (Test-Path $msbuild)) {
    throw "ไม่พบ MSBuild ที่ $msbuild"
}

Write-Host ''
Write-Host '=== 1/3  รัน SQL scripts ===' -ForegroundColor Cyan

Get-ChildItem (Join-Path $root 'src\Database\*.sql') | Sort-Object Name | ForEach-Object {
    Write-Host ("   -> " + $_.Name)

    # -f 65001 สำคัญมาก : ไฟล์ .sql เป็น UTF-8 แต่ sqlcmd จะอ่านด้วย codepage
    # ของ Windows (874) ถ้าไม่บอก ทำให้ข้อความไทยใน seed data เพี้ยนตั้งแต่ตอน insert
    & sqlcmd -S $SqlServer -E -b -f 65001 -i $_.FullName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw ("SQL script ล้มเหลว: " + $_.Name)
    }
}

Write-Host ''
Write-Host '=== 2/3  Build solution ===' -ForegroundColor Cyan

& $msbuild (Join-Path $root 'Messenger.sln') -t:restore,build -v:m -nologo
if ($LASTEXITCODE -ne 0) {
    throw 'build ไม่ผ่าน'
}

Write-Host ''
Write-Host '=== 3/3  Unit tests ===' -ForegroundColor Cyan

& dotnet test (Join-Path $root 'tests\UnitTests\Messenger.UnitTests.csproj') --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'unit test ไม่ผ่าน'
}

Write-Host ''
Write-Host 'ผ่านทั้งหมด' -ForegroundColor Green
Write-Host 'รันเว็บด้วย:' -ForegroundColor Green
Write-Host ('  & "C:\Program Files\IIS Express\iisexpress.exe" /path:"' + (Join-Path $root 'src\Web') + '" /port:52080')
