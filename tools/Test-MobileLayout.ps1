<#
    Test-MobileLayout.ps1 — UAT 8.1–8.3 : หน้าจอบนมือถือ (กว้าง 390px)

    ⚠ ตรวจด้วย **device emulation ของ Chrome** ไม่ใช่มือถือจริง
    ครอบคลุมเฉพาะเรื่องที่ layout engine ตัดสิน :
      8.1  ไม่มีหน้าไหนต้องเลื่อนซ้าย-ขวา · ตารางเลื่อนได้ในกรอบของตัวเอง
      8.2  เมนู hamburger กางออกครบทุกรายการตามสิทธิ์ และกดโดนทุกอัน
      8.3  ปุ่มดำเนินการในหน้าคิวงานไม่ทับกัน · ขนาดพอให้นิ้วกด · กดแล้วโดนตัวเอง
    ส่วน 8.4 (เปิดกล้องหลัง) และ 8.5 (ปฏิทินของเครื่อง) เป็นพฤติกรรมของ OS มือถือ
    จำลองไม่ได้ ต้องใช้เครื่องจริงเท่านั้น

    ใช้:  pwsh -File tools\Test-MobileLayout.ps1
    ต้องมี : เว็บรันอยู่ที่ http://localhost:52080 · Chrome หรือ Edge · Node.js 22 ขึ้นไป
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:52080'
)

$ErrorActionPreference = 'Stop'
$script:Passed = 0
$script:Failed = 0

$probe = Join-Path $PSScriptRoot 'mobile-layout-probe.mjs'

# ปุ่มที่เตี้ยกว่านี้ถือว่าเล็กเกินกว่านิ้วจะกดแม่น (WCAG 2.2 AA ระบุ 24×24 เป็นขั้นต่ำ)
$minTouch = 24

function Assert([string] $title, [bool] $condition, [string] $detail = '') {
    if ($condition) { Write-Host "   [ผ่าน] $title" -ForegroundColor Green; $script:Passed++ }
    else { Write-Host "   [ไม่ผ่าน] $title $detail" -ForegroundColor Red; $script:Failed++ }
}

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

function Invoke-Probe([string] $empCode, [string[]] $paths, [string] $queuePath) {
    $args = @($probe, $BaseUrl, $empCode, ($paths -join ','))
    if ($queuePath) { $args += $queuePath }
    $output = & node @args 2>&1
    $line = $output | Where-Object { "$_" -like '===RESULT===*' } | Select-Object -First 1
    if (-not $line) { throw "probe ไม่คืนผล : $($output -join [Environment]::NewLine)" }
    return ("$line" -replace '^===RESULT===', '') | ConvertFrom-Json
}

if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'ไม่พบ Node.js — สคริปต์นี้ต้องใช้ Node 22 ขึ้นไป' }

# ---------------------------------------------------------------- เตรียมข้อมูลให้ทุกหน้ามีของจริงให้ดู

Write-Host ''
Write-Host '=== เตรียมใบงานสำหรับให้แต่ละหน้ามีข้อมูลแสดง ===' -ForegroundColor Cyan

$user = New-Session '10002'        # U-User สาขา SDC (เจ้าของใบงาน)
$messenger = New-Session '10003'   # M-Messenger สาขา SDC

function New-TestRequest($session) {
    $page = Invoke-WebRequest "$BaseUrl/Requests/Create" -WebSession $session -UseBasicParsing
    $created = Invoke-WebRequest "$BaseUrl/Requests/Create" -Method Post -WebSession $session -UseBasicParsing -Body @{
        '__RequestVerificationToken' = (Get-Token $page)
        'RequesterEmpCode'           = '10002'
        'SendDate'                   = (Get-Date).ToString('yyyy-MM-dd')
        'ContactName'                = 'บริษัท ทดสอบหน้าจอมือถือ จำกัด (ชื่อยาวเพื่อดูว่าตารางล้นจอไหม)'
        'Address'                    = '199/28 อาคารทดสอบ ชั้น 14 ถนนสุขุมวิท แขวงคลองเตย เขตคลองเตย กรุงเทพมหานคร 10110'
        'Detail'                     = 'ใบงานสำหรับทดสอบ UAT หมวด 8 — ข้อความยาวพอสมควรเพื่อให้เห็นการตัดบรรทัด'
        'Phone'                      = '02-123-4567 ต่อ 890'
        'IsPersonal'                 = 'false'
        'JobTypes[0].Code' = 'SendDoc';      'JobTypes[0].Selected' = 'true'
        'JobTypes[0].DetailText' = 'เอกสารสัญญาฉบับจริง 3 ชุด'
        'JobTypes[1].Code' = 'ReceiveDoc';   'JobTypes[1].Selected' = 'true'
        'JobTypes[1].DetailText' = 'รับใบเสร็จกลับมาด้วย'
        'JobTypes[2].Code' = 'ReceiveCheck'; 'JobTypes[2].Selected' = 'false'
        'JobTypes[3].Code' = 'PlaceBill';    'JobTypes[3].Selected' = 'false'
        'JobTypes[4].Code' = 'RenewTax';     'JobTypes[4].Selected' = 'false'
        'JobTypes[5].Code' = 'Other';        'JobTypes[5].Selected' = 'false'
    }
    if ($created.BaseResponse.RequestMessage.RequestUri.AbsolutePath -match '/Requests/Details/(\d+)') { return [int] $Matches[1] }
    throw 'สร้างใบงานทดสอบไม่สำเร็จ'
}

$receivedId = New-TestRequest $user      # คงสถานะ "รับแจ้ง" ไว้ให้หน้าแก้ไขเปิดได้
$deliveringId = New-TestRequest $user

$detailsPage = Invoke-WebRequest "$BaseUrl/Requests/Details/$deliveringId" -WebSession $messenger -UseBasicParsing
Invoke-WebRequest "$BaseUrl/Requests/ChangeStatus" -Method Post -WebSession $messenger -UseBasicParsing -Body @{
    '__RequestVerificationToken' = (Get-Token $detailsPage)
    'id' = "$deliveringId"; 'statusAction' = 'Confirm'; 'returnTo' = 'details' } | Out-Null

Write-Host "   ใบงาน reqId=$receivedId (รับแจ้ง) · reqId=$deliveringId (กำลังส่ง)" -ForegroundColor DarkGray

$today = (Get-Date).ToString('yyyy-MM-dd')
$commonPaths = @(
    '/',
    '/Requests',
    '/Requests/Create',
    "/Requests/Details/$deliveringId",
    "/Requests/Edit/$receivedId",
    "/Reports?dateFrom=$today&dateTo=$today"
)

# ---------------------------------------------------------------- ตรวจทีละ role

$roles = @(
    @{ Code = '10003'; Name = 'M-Messenger สาขา SDC'; Queue = "/Queue?date=$today"; Menu = 4 }
    @{ Code = '10001'; Name = 'A-Admin สาขา SDC';     Queue = "/Queue?date=$today"; Menu = 4 }
    @{ Code = '10002'; Name = 'U-User สาขา SDC';      Queue = $null;                Menu = 3 }
)

foreach ($role in $roles) {
    Write-Host ''
    Write-Host "=== $($role.Name) — จอ 390x844 ===" -ForegroundColor Cyan

    $paths = $commonPaths
    if ($role.Queue) { $paths = $commonPaths + $role.Queue }
    $r = Invoke-Probe $role.Code $paths $role.Queue

    Assert 'probe ทำงานจบโดยไม่มีข้อผิดพลาด' ($r.ok -eq $true) "($($r.error))"
    if (-not $r.ok) { continue }

    # ---- 8.1 ----
    foreach ($page in $r.pages) {
        $name = if ($page.path) { $page.path } else { '/Account/Login' }
        $noHScroll = $page.pageScrollWidth -le $page.innerWidth + 1
        Assert "8.1 $name ไม่ต้องเลื่อนซ้าย-ขวา" $noHScroll `
            "(scrollWidth $($page.pageScrollWidth) > จอ $($page.innerWidth))"

        if ($page.overflowing.Count -gt 0) {
            $names = ($page.overflowing | ForEach-Object { "$($_.el) [$($_.left)..$($_.right)]" }) -join ' · '
            Assert "8.1 $name ไม่มี element ยื่นพ้นขอบจอ" $false "→ $names"
        }

        foreach ($t in $page.tables) {
            Assert "8.1 $name ตาราง $($t.table) อยู่ในกรอบที่เลื่อนได้เอง" $t.hasScrollContainer `
                "(กว้าง $($t.width)px แต่ไม่มี .table-responsive ครอบ)"
        }
    }

    # ---- 8.2 ----
    Assert '8.2 มีปุ่ม hamburger และเห็นได้ที่จอ 390px' ($r.menu.hasToggler -and $r.menu.togglerVisible)
    Assert '8.2 เมนูยุบอยู่ก่อนกด' ($r.menu.collapsedBefore -eq $true)
    Assert '8.2 กดแล้วเมนูกางออก' ($r.menu.shown -eq $true)

    $links = $r.menu.items | Where-Object { $_.text -ne 'ออกจากระบบ' }
    $texts = ($r.menu.items | ForEach-Object { $_.text }) -join ' · '
    Write-Host "   เมนูที่กางออก : $texts" -ForegroundColor DarkGray
    Assert "8.2 มีรายการเมนูครบตามสิทธิ์ ($($role.Menu) รายการ + ออกจากระบบ)" ($links.Count -eq $role.Menu) `
        "(ได้ $($links.Count))"
    Assert '8.2 ทุกรายการในเมนูกดโดนตัวเอง (ไม่มีอะไรบัง)' `
        (($r.menu.items | Where-Object { -not $_.hitsSelf }).Count -eq 0) `
        "(กดไม่โดน: $(($r.menu.items | Where-Object { -not $_.hitsSelf } | ForEach-Object { $_.text }) -join ', '))"

    if ($role.Code -eq '10002') {
        Assert '8.2 U-User ไม่เห็นเมนูคิวงาน (§5)' (($r.menu.items | Where-Object { $_.text -match 'คิวงาน' }).Count -eq 0)
    } else {
        Assert '8.2 เห็นเมนูคิวงานประจำวัน' (($r.menu.items | Where-Object { $_.text -match 'คิวงาน' }).Count -eq 1)
    }

    # ---- 8.3 ----
    if ($role.Queue) {
        Write-Host "   ปุ่มในหน้าคิวงาน $($r.buttons.count) ปุ่ม" -ForegroundColor DarkGray
        Assert '8.3 หน้าคิวงานมีปุ่มดำเนินการให้กด' ($r.buttons.count -gt 0)
        Assert '8.3 ไม่มีปุ่มไหนทับกัน' ($r.buttons.overlaps.Count -eq 0) `
            "(ทับกัน: $(($r.buttons.overlaps | ForEach-Object { $_ -join ' ↔ ' }) -join ' · '))"
        Assert "8.3 ทุกปุ่มใหญ่พอให้นิ้วกด (≥ ${minTouch}px)" ($r.buttons.tooSmall.Count -eq 0) `
            "(เล็กเกิน: $($r.buttons.tooSmall -join ' · '))"
        Assert '8.3 ทุกปุ่มที่ใช้งานได้ กดแล้วโดนตัวเอง' ($r.buttons.notHit.Count -eq 0) `
            "(กดไม่โดน: $($r.buttons.notHit -join ' · '))"
        Write-Host "   (ในนี้มีปุ่มที่ถูก disabled อยู่ $($r.buttons.disabledCount) ปุ่ม — ปุ่มเลื่อนลำดับของแถวแรก/แถวสุดท้าย ไม่นับว่ากดไม่โดน)" -ForegroundColor DarkGray

        if ($r.buttons.underThumb.Count -gt 0) {
            $distinct = $r.buttons.underThumb | Sort-Object -Unique
            Write-Host '   [ข้อสังเกต] ปุ่มที่ด้านใดด้านหนึ่งเล็กกว่า 44px ที่ Apple/Google แนะนำ (ยังกดได้ ไม่นับว่าไม่ผ่าน) :' -ForegroundColor Yellow
            Write-Host "      $($distinct -join ' · ')" -ForegroundColor Yellow
        }
    }
}

Write-Host ''
Write-Host '⚠ ทั้งหมดนี้ตรวจด้วย device emulation ไม่ใช่มือถือจริง — 8.4/8.5 ยังต้องใช้เครื่องจริง' -ForegroundColor Yellow
Write-Host ''
Write-Host "สรุป : ผ่าน $script:Passed · ไม่ผ่าน $script:Failed" -ForegroundColor $(if ($script:Failed -eq 0) { 'Green' } else { 'Red' })
exit ([int] ($script:Failed -gt 0))
