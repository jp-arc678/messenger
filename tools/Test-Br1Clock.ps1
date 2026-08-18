<#
    Test-Br1Clock.ps1 — UAT 2.4 : พิสูจน์เส้นแบ่ง 10:00 ของ BR-1 ด้วยนาฬิกาจำลอง
    ตั้ง appSetting ClockOffsetMinutes ให้ "เวลาที่ระบบมองเห็น" ตกคร่อม 10:00 ทั้งสองฝั่ง
    แล้วดูว่า sendDate ที่ระบบคำนวณให้ (ค่า default ในฟอร์ม + ค่าที่บันทึกจริง) ถูกไหม
    ⚠ คืนค่า Web.config กลับเป็นค่าว่างเสมอเมื่อจบ (ทั้งกรณีสำเร็จและพัง)
#>
[CmdletBinding()]
param([string] $BaseUrl = 'http://localhost:52080')

$ErrorActionPreference = 'Stop'
$configPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Web\Web.config'
$script:Passed = 0
$script:Failed = 0

function Get-Token($response) {
    if ($response.Content -match 'name="__RequestVerificationToken"[^>]*value="([^"]+)"') { return $Matches[1] }
    throw 'ไม่พบ __RequestVerificationToken'
}

function New-Session([string] $empCode) {
    $session = $null
    $login = Invoke-WebRequest "$BaseUrl/Account/Login" -SessionVariable session -UseBasicParsing
    Invoke-WebRequest "$BaseUrl/Account/Login" -Method Post -WebSession $session -UseBasicParsing -Body @{
        '__RequestVerificationToken' = (Get-Token $login); 'EmpCode' = $empCode } | Out-Null
    return $session
}

function Assert([string] $title, [bool] $condition, [string] $detail = '') {
    if ($condition) { Write-Host "   [ผ่าน] $title" -ForegroundColor Green; $script:Passed++ }
    else { Write-Host "   [ไม่ผ่าน] $title $detail" -ForegroundColor Red; $script:Failed++ }
}

function Set-ClockOffset([string] $value) {
    $text = [IO.File]::ReadAllText($configPath, [Text.Encoding]::UTF8)
    $updated = [regex]::Replace($text, '(<add key="ClockOffsetMinutes" value=")[^"]*(" />)', "`${1}$value`${2}")
    if ($updated -eq $text -and $value -ne '') { throw 'แก้ค่า ClockOffsetMinutes ใน Web.config ไม่สำเร็จ' }
    [IO.File]::WriteAllText($configPath, $updated, (New-Object Text.UTF8Encoding $false))
    # แก้ Web.config = แอปถูก recycle — รอจนหน้าเว็บตอบอีกครั้ง
    Start-Sleep -Seconds 2
    for ($i = 0; $i -lt 30; $i++) {
        try { Invoke-WebRequest "$BaseUrl/Account/Login" -UseBasicParsing -TimeoutSec 30 | Out-Null; return }
        catch { Start-Sleep -Seconds 1 }
    }
    throw 'เว็บไม่กลับมาหลังแก้ Web.config'
}

function Get-DefaultSendDate($session) {
    $page = Invoke-WebRequest "$BaseUrl/Requests/Create" -WebSession $session -UseBasicParsing
    if ($page.Content -match 'id="SendDate"[^>]*value="([^"]*)"') { return $Matches[1] }
    throw 'อ่านค่า default ของช่องวันที่ส่งไม่ได้'
}

function New-Request($session, [string] $sendDate) {
    $page = Invoke-WebRequest "$BaseUrl/Requests/Create" -WebSession $session -UseBasicParsing
    $created = Invoke-WebRequest "$BaseUrl/Requests/Create" -Method Post -WebSession $session -UseBasicParsing -Body @{
        '__RequestVerificationToken' = (Get-Token $page)
        'RequesterEmpCode'           = '10002'
        'SendDate'                   = $sendDate
        'ContactName'                = 'บริษัท ทดสอบนาฬิกา จำกัด'
        'Address'                    = '1 ถนนทดสอบ BR-1'
        'Detail'                     = 'ใบงานสำหรับทดสอบ UAT 2.4'
        'IsPersonal'                 = 'false'
        'JobTypes[0].Code' = 'SendDoc';      'JobTypes[0].Selected' = 'true'
        'JobTypes[1].Code' = 'ReceiveDoc';   'JobTypes[1].Selected' = 'false'
        'JobTypes[2].Code' = 'ReceiveCheck'; 'JobTypes[2].Selected' = 'false'
        'JobTypes[3].Code' = 'PlaceBill';    'JobTypes[3].Selected' = 'false'
        'JobTypes[4].Code' = 'RenewTax';     'JobTypes[4].Selected' = 'false'
        'JobTypes[5].Code' = 'Other';        'JobTypes[5].Selected' = 'false'
    }
    if ($created.Content -match '>วันที่ส่ง<[\s\S]{0,200}?(\d{2}/\d{2}/\d{4})') { return $Matches[1] }
    throw 'อ่านวันที่ส่งจากหน้ารายละเอียดไม่ได้'
}

# วันทำการถัดไปจากวันที่กำหนด (BR-1 ข้อ 3 — เสาร์/อาทิตย์ เลื่อนเป็นจันทร์)
function Get-NextWorkingDay([datetime] $d) {
    $next = $d.AddDays(1)
    while ($next.DayOfWeek -in @([DayOfWeek]::Saturday, [DayOfWeek]::Sunday)) { $next = $next.AddDays(1) }
    return $next
}

try {
    $now = Get-Date
    Write-Host ''
    Write-Host "เวลาจริงตอนนี้ $($now.ToString('dd/MM/yyyy HH:mm'))" -ForegroundColor DarkGray

    # เลือก offset ให้ระบบมองเห็นเวลา 09:15 และ 10:45 ของ "วันเดียวกัน"
    $beforeTen = [int] (([datetime]::Today.AddHours(9).AddMinutes(15)) - $now).TotalMinutes
    $afterTen  = [int] (([datetime]::Today.AddHours(10).AddMinutes(45)) - $now).TotalMinutes

    $today = [datetime]::Today
    # ถ้าวันนี้เป็นเสาร์/อาทิตย์ ค่า default ของกรณี "ก่อน 10:00" ต้องเลื่อนเป็นจันทร์ด้วย
    $expectBefore = if ($today.DayOfWeek -in @([DayOfWeek]::Saturday, [DayOfWeek]::Sunday)) {
        Get-NextWorkingDay $today.AddDays(-1) } else { $today }
    $expectAfter = Get-NextWorkingDay $today

    $user = New-Session '10002'

    Write-Host ''
    Write-Host "=== ก่อน 10:00 (ClockOffsetMinutes = $beforeTen → ระบบมองว่า 09:15) ===" -ForegroundColor Cyan
    Set-ClockOffset "$beforeTen"
    $user = New-Session '10002'
    $default = Get-DefaultSendDate $user
    Assert "ค่า default ในฟอร์ม = $($expectBefore.ToString('yyyy-MM-dd')) (วันนี้)" `
        ($default -eq $expectBefore.ToString('yyyy-MM-dd')) "(ได้ $default)"
    $saved = New-Request $user $default
    Assert "ใบงานที่บันทึกมีวันที่ส่ง = $($expectBefore.ToString('dd/MM/yyyy'))" `
        ($saved -eq $expectBefore.ToString('dd/MM/yyyy')) "(ได้ $saved)"

    Write-Host ''
    Write-Host "=== หลัง 10:00 (ClockOffsetMinutes = $afterTen → ระบบมองว่า 10:45) ===" -ForegroundColor Cyan
    Set-ClockOffset "$afterTen"
    $user = New-Session '10002'
    $default = Get-DefaultSendDate $user
    Assert "ค่า default ในฟอร์ม = $($expectAfter.ToString('yyyy-MM-dd')) (วันทำการถัดไป)" `
        ($default -eq $expectAfter.ToString('yyyy-MM-dd')) "(ได้ $default)"
    $saved = New-Request $user $default
    Assert "ใบงานที่บันทึกมีวันที่ส่ง = $($expectAfter.ToString('dd/MM/yyyy'))" `
        ($saved -eq $expectAfter.ToString('dd/MM/yyyy')) "(ได้ $saved)"
}
finally {
    Write-Host ''
    Write-Host '=== คืนค่า ClockOffsetMinutes เป็นค่าว่าง ===' -ForegroundColor Cyan
    Set-ClockOffset ''
    $text = [IO.File]::ReadAllText($configPath, [Text.Encoding]::UTF8)
    Assert 'Web.config กลับเป็น ClockOffsetMinutes ว่างแล้ว' ($text -match '<add key="ClockOffsetMinutes" value="" />')
    $user = New-Session '10002'
    $default = Get-DefaultSendDate $user
    Write-Host "   ค่า default ของฟอร์มหลังคืนค่า = $default (เวลาจริง)" -ForegroundColor DarkGray
}

Write-Host ''
Write-Host "สรุป : ผ่าน $script:Passed · ไม่ผ่าน $script:Failed" -ForegroundColor $(if ($script:Failed -eq 0) { 'Green' } else { 'Red' })
exit ([int] ($script:Failed -gt 0))
