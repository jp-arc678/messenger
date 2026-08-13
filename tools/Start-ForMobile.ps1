<#
.SYNOPSIS
    รันเว็บด้วย IIS Express แบบให้เครื่องอื่นในวง LAN (มือถือ/แท็บเล็ต) เข้าได้ — สำหรับ UAT หมวด 8

.DESCRIPTION
    ปกติ `iisexpress /path:... /port:52080` จะผูกกับ localhost เท่านั้น เครื่องอื่นเข้าไม่ได้
    สคริปต์นี้จัดการ 3 อย่างที่ต้องมีครบถึงจะเข้าจากมือถือได้ :

      1. binding  — สร้าง applicationhost.config ของตัวเอง ผูกกับ *:52080 (ทุก network interface)
      2. urlacl   — จองสิทธิ์ฟัง URL นั้นให้ผู้ใช้ปัจจุบัน (ไม่งั้น IIS Express เปิดไม่ขึ้น)
      3. firewall — เปิด TCP 52080 ขาเข้า เฉพาะโปรไฟล์ Private/Domain

    ข้อ 2 และ 3 ต้องใช้สิทธิ์ Administrator แต่ทำครั้งเดียวจบ ครั้งต่อไปรันธรรมดาก็พอ

.NOTES
    ⚠ ระบบยังใช้ SSO จำลอง (D3) — หน้า login ให้พิมพ์รหัสพนักงานเข้าได้เลยโดยไม่ต้องมีรหัสผ่าน
      เมื่อเปิดให้เข้าจาก LAN ได้ แปลว่า **ใครก็ตามที่อยู่วงเดียวกันเข้าระบบเป็นใครก็ได้**
      ใช้เพื่อทดสอบเท่านั้น อย่าเปิดค้างไว้ และอย่าใช้บนเครือข่ายสาธารณะ

.EXAMPLE
    # ครั้งแรก (ต้อง Run as Administrator)
    pwsh -File tools\Start-ForMobile.ps1

.EXAMPLE
    # ครั้งต่อไป (ไม่ต้อง admin ถ้าตั้งค่าไว้แล้ว)
    pwsh -File tools\Start-ForMobile.ps1
#>
[CmdletBinding()]
param(
    [int] $Port = 52080,

    # ข้ามการตั้งค่า urlacl/firewall (ใช้เมื่อรู้ว่าตั้งไว้แล้วและไม่ได้เปิดด้วยสิทธิ์ admin)
    [switch] $SkipSetup
)

$ErrorActionPreference = 'Stop'

$root      = Split-Path $PSScriptRoot -Parent
$sitePath  = Join-Path $root 'src\Web'
$outDir    = Join-Path $PSScriptRoot '.iisexpress'
$outConfig = Join-Path $outDir 'applicationhost.config'
$template  = 'C:\Program Files\IIS Express\config\templates\PersonalWebServer\applicationhost.config'
$exe       = 'C:\Program Files\IIS Express\iisexpress.exe'
$urlAcl    = "http://*:$Port/"

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal] $id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

foreach ($required in @($template, $exe)) {
    if (-not (Test-Path $required)) {
        throw "ไม่พบ $required — ติดตั้ง IIS Express ก่อน"
    }
}

if (-not (Test-Path $sitePath)) {
    throw "ไม่พบโฟลเดอร์เว็บ: $sitePath"
}

# ---------- 1. สร้าง applicationhost.config ของโปรเจกต์ ----------
# ตั้งใจไม่แก้ config กลางที่ Documents\IISExpress เพราะนั่นใช้ร่วมกับโปรเจกต์อื่น
# ไฟล์นี้ generate ใหม่ทุกครั้ง จึงไม่ต้องเก็บเข้า git (ดู .gitignore)

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Copy-Item $template $outConfig -Force

[xml] $config = Get-Content $outConfig
$sites = $config.configuration.'system.applicationHost'.sites

# เอา site ตัวอย่างของ template ออกให้หมด เหลือเฉพาะของเรา
foreach ($existing in @($sites.site)) {
    $sites.RemoveChild($existing) | Out-Null
}

$site = $config.CreateElement('site')
$site.SetAttribute('name', 'Messenger')
$site.SetAttribute('id', '1')

$application = $config.CreateElement('application')
$application.SetAttribute('path', '/')
$application.SetAttribute('applicationPool', 'Clr4IntegratedAppPool')

$vdir = $config.CreateElement('virtualDirectory')
$vdir.SetAttribute('path', '/')
$vdir.SetAttribute('physicalPath', $sitePath)
$application.AppendChild($vdir) | Out-Null
$site.AppendChild($application) | Out-Null

$bindings = $config.CreateElement('bindings')
$binding = $config.CreateElement('binding')
$binding.SetAttribute('protocol', 'http')
# "*" = ฟังทุก interface — จุดสำคัญที่ทำให้เครื่องอื่นในวงเข้าได้
$binding.SetAttribute('bindingInformation', "*:${Port}:")
$bindings.AppendChild($binding) | Out-Null
$site.AppendChild($bindings) | Out-Null

$sites.AppendChild($site) | Out-Null
$config.Save($outConfig)

Write-Host "[1/3] สร้าง config แล้ว : $outConfig" -ForegroundColor Green
Write-Host "      ผูกกับ *:$Port -> $sitePath"

# ---------- 2 + 3. urlacl และ firewall (ต้อง admin) ----------
$aclExists = (netsh http show urlacl url=$urlAcl 2>$null | Select-String -SimpleMatch $urlAcl) -ne $null
$fwName    = "Messenger dev (IIS Express $Port)"
$fwExists  = $null -ne (Get-NetFirewallRule -DisplayName $fwName -ErrorAction SilentlyContinue)

if ($SkipSetup) {
    Write-Host "[2/3] ข้ามการตั้ง urlacl/firewall ตามที่สั่ง" -ForegroundColor Yellow
}
elseif ($aclExists -and $fwExists) {
    Write-Host "[2/3] urlacl และ firewall ตั้งไว้แล้ว ไม่ต้องทำซ้ำ" -ForegroundColor Green
}
elseif (-not (Test-Admin)) {
    Write-Host ''
    Write-Host 'ยังตั้งค่าไม่ครบ และหน้าต่างนี้ไม่ได้เป็น Administrator' -ForegroundColor Yellow
    Write-Host 'เปิด PowerShell แบบ Run as administrator แล้วรันสองคำสั่งนี้ (ทำครั้งเดียวจบ) :' -ForegroundColor Yellow
    Write-Host ''
    if (-not $aclExists) {
        Write-Host "  netsh http add urlacl url=$urlAcl user=$env:USERDOMAIN\$env:USERNAME"
    }
    if (-not $fwExists) {
        Write-Host "  New-NetFirewallRule -DisplayName '$fwName' -Direction Inbound ``"
        Write-Host "      -Protocol TCP -LocalPort $Port -Action Allow -Profile Private,Domain"
    }
    Write-Host ''
    Write-Host 'แล้วค่อยรันสคริปต์นี้ใหม่' -ForegroundColor Yellow
    exit 1
}
else {
    if (-not $aclExists) {
        netsh http add urlacl url=$urlAcl user="$env:USERDOMAIN\$env:USERNAME" | Out-Null
        Write-Host "[2/3] เพิ่ม urlacl $urlAcl แล้ว" -ForegroundColor Green
    }
    if (-not $fwExists) {
        # จำกัดเฉพาะ Private/Domain — ไม่เปิดบนเครือข่ายสาธารณะ
        New-NetFirewallRule -DisplayName $fwName -Direction Inbound -Protocol TCP `
            -LocalPort $Port -Action Allow -Profile Private, Domain | Out-Null
        Write-Host "[3/3] เพิ่ม firewall rule '$fwName' แล้ว" -ForegroundColor Green
    }
}

# ---------- แสดง URL ที่ใช้บนมือถือ ----------
Write-Host ''
Write-Host 'เปิดบนมือถือที่ URL นี้ (ต้องต่อ Wi-Fi วงเดียวกับเครื่องนี้) :' -ForegroundColor Cyan

Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
    Sort-Object InterfaceAlias |
    ForEach-Object {
        Write-Host ("   http://{0}:{1}/    ({2})" -f $_.IPAddress, $Port, $_.InterfaceAlias) -ForegroundColor White
    }

Write-Host ''
Write-Host 'SSO ยังเป็นของจำลอง (D3) — ใครอยู่วงเดียวกันก็ล็อกอินเป็นใครก็ได้' -ForegroundColor Yellow
Write-Host 'ใช้ทดสอบเท่านั้น ปิดเมื่อเสร็จ (Ctrl+C)' -ForegroundColor Yellow
Write-Host ''

& $exe /config:$outConfig /site:Messenger
