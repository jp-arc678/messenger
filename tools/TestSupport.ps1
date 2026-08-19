<#
    TestSupport.ps1 — ฟังก์ชันร่วมของสคริปต์ทดสอบที่ "ยิงของจริงใส่เว็บ"

    ใช้ด้วยการ dot-source ไว้ต้นสคริปต์ :   . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    เมื่อ dot-source แล้ว ฟังก์ชันในนี้จะอยู่ใน scope ของสคริปต์ผู้เรียก
    ตัวนับ $script:Passed / $script:Failed จึงเป็นของสคริปต์นั้นเอง

    เหตุผลที่แยกไฟล์ : สคริปต์ทดสอบ 5-6 ตัวเคยก๊อป Get-Token/New-Session/Assert
    ไปคนละชุด พอแก้ตัวหนึ่งอีกตัวก็เพี้ยน — รวมไว้ที่เดียวจบ

    เรื่องการล้างข้อมูล : สคริปต์ทดสอบสร้างใบงานจริงลงฐานข้อมูล ถ้าไม่ล้าง
    ฐาน dev จะบวมด้วยขยะเรื่อย ๆ (ปัญหาที่เจอจริงหลัง UAT รอบ 2)
    วิธีที่ใช้คือจำ ReqId สูงสุด "ก่อน" เริ่มทดสอบ แล้วลบทุกใบที่เกิดหลังจากนั้นตอนจบ
    · ตาราง tblRequestJobType / tblMessengerAssignment / tblDeliveryPhoto /
      tblStatusHistory / tblPauseReason / tblCancelReason ตั้ง ON DELETE CASCADE ไว้แล้ว
      ลบใบงานใบเดียวจึงพาลูกไปหมด
    · ไฟล์รูปบน filesystem ไม่มีใครลบให้ ต้องลบเองก่อนลบแถว
    · **ไม่ล้างเมื่อมีข้อที่ไม่ผ่าน** — ข้อมูลตอนพังคือหลักฐานที่ต้องเปิดดู
#>

$ErrorActionPreference = 'Stop'

$script:Passed = 0
$script:Failed = 0

# ---------------------------------------------------------------- HTTP

function Get-Token($response) {
    if ($response.Content -match 'name="__RequestVerificationToken"[^>]*value="([^"]+)"') { return $Matches[1] }
    throw 'ไม่พบ __RequestVerificationToken'
}

<#
    เข้าสู่ระบบด้วย SSO จำลอง (D3 — ไม่มีรหัสผ่าน) แล้วคืน WebSession ที่ถือ cookie ไว้
    $BaseUrl มาจาก scope ของสคริปต์ผู้เรียก
#>
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

# ---------------------------------------------------------------- ฐานข้อมูล

function Invoke-Sql([string] $query) {
    $out = & sqlcmd -S localhost -d MessengerDb -h -1 -W -b -Q "SET NOCOUNT ON; $query" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd ล้มเหลว : $($out -join [Environment]::NewLine)" }
    return @($out | Where-Object { "$_".Trim() -ne '' })
}

<#
    ReqId สูงสุดในฐานตอนนี้ — ใช้เป็นเส้นแบ่งว่าอะไรคือ "ของที่เทสต์สร้าง"
    เรียกก่อนเริ่มทดสอบเสมอ
#>
function Get-MaxReqId {
    $value = (Invoke-Sql 'SELECT ISNULL(MAX(ReqId), 0) FROM dbo.tblDeliveryRequest' | Select-Object -First 1)
    return [int] $value
}

<# โฟลเดอร์เก็บรูปตาม D25 — ค่าว่างใน Web.config = ~\App_Data\Photos ของเว็บ #>
function Get-PhotoRoot {
    $webRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Web'
    $configured = ''
    $configPath = Join-Path $webRoot 'Web.config'
    if (Test-Path $configPath) {
        $xml = [xml] (Get-Content $configPath -Raw)
        $node = $xml.configuration.appSettings.add | Where-Object { $_.key -eq 'PhotoStorageRoot' }
        if ($node) { $configured = "$($node.value)".Trim() }
    }
    if (-not $configured) { return Join-Path $webRoot 'App_Data\Photos' }
    if ($configured.StartsWith('~')) { return Join-Path $webRoot ($configured.TrimStart('~', '\', '/')) }
    return $configured
}

<#
    ลบใบงานทุกใบที่เกิดหลังเส้นแบ่ง พร้อมไฟล์รูปของมัน

    ⚠ อย่ารันสคริปต์ทดสอบพร้อมกับที่มีคนใช้เว็บอยู่ — ใบที่คนอื่นสร้างระหว่างนั้น
      จะอยู่เหนือเส้นแบ่งด้วยแล้วโดนลบไปด้วย (ฐาน dev เท่านั้น จึงยอมรับความเสี่ยงนี้)
#>
function Remove-TestData([int] $SinceReqId) {
    $rows = Invoke-Sql "SELECT ReqId FROM dbo.tblDeliveryRequest WHERE ReqId > $SinceReqId"
    if ($rows.Count -eq 0) {
        Write-Host '   (ไม่มีใบงานที่ต้องลบ)' -ForegroundColor DarkGray
        return
    }

    $photoRoot = Get-PhotoRoot
    $files = Invoke-Sql "SELECT FilePath FROM dbo.tblDeliveryPhoto WHERE ReqId > $SinceReqId"
    $removedFiles = 0
    foreach ($relative in $files) {
        $full = Join-Path $photoRoot "$relative".Trim()
        if (Test-Path $full) {
            Remove-Item $full -Force -ErrorAction SilentlyContinue
            $removedFiles++
        }
    }

    # ลูกทุกตารางเป็น ON DELETE CASCADE จึงลบแถวแม่พอ
    Invoke-Sql "DELETE FROM dbo.tblDeliveryRequest WHERE ReqId > $SinceReqId" | Out-Null

    Write-Host "   ลบใบงานทดสอบ $($rows.Count) ใบ และไฟล์รูป $removedFiles ไฟล์" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------- จบการทดสอบ

<#
    พิมพ์สรุป · ล้างข้อมูลถ้าควรล้าง · คืน exit code
    เรียกเป็นบรรทัดสุดท้ายของสคริปต์ทดสอบ :  exit (Complete-TestRun $since -KeepData:$KeepData)
#>
function Complete-TestRun([int] $SinceReqId, [switch] $KeepData) {
    Write-Host ''
    if ($KeepData) {
        Write-Host '=== ไม่ล้างข้อมูลทดสอบ (สั่ง -KeepData ไว้) ===' -ForegroundColor Cyan
    }
    elseif ($script:Failed -gt 0) {
        Write-Host '=== ไม่ล้างข้อมูลทดสอบ เพราะมีข้อที่ไม่ผ่าน — เก็บไว้ให้เปิดดูสาเหตุ ===' -ForegroundColor Yellow
        Write-Host "   ใบงานที่เทสต์สร้างคือ ReqId มากกว่า $SinceReqId" -ForegroundColor Yellow
    }
    else {
        Write-Host '=== ล้างข้อมูลทดสอบ ===' -ForegroundColor Cyan
        Remove-TestData $SinceReqId
    }

    Write-Host ''
    Write-Host "สรุป : ผ่าน $script:Passed · ไม่ผ่าน $script:Failed" -ForegroundColor $(if ($script:Failed -eq 0) { 'Green' } else { 'Red' })
    return [int] ($script:Failed -gt 0)
}
