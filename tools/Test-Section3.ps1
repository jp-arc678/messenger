<#
    ทดสอบ UAT หมวด 3 — คิวงาน + สถานะ (Phase 2 · §6, D7, D11)
    ใช้วันที่ส่ง = พรุ่งนี้ ซึ่งคิวยังว่างสนิท ลำดับจึงเริ่มที่ 1 ตรงตามเกณฑ์ 3.2

    ใช้:  pwsh -File tools\Test-Section3.ps1
    ต้องมี : เว็บรันอยู่ที่ http://localhost:52080 · sqlcmd (ใช้ล้างข้อมูลทดสอบตอนจบ)
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:52080',

    # ไม่ล้างใบงานที่เทสต์สร้าง (ค่าปกติจะล้างให้เมื่อผ่านครบทุกข้อ)
    [switch] $KeepData
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestSupport.ps1')

# เส้นแบ่งว่าอะไรคือ "ใบงานที่เทสต์นี้สร้าง" — ต้องอ่านก่อนเริ่มยิงอะไรทั้งสิ้น
$sinceReqId = Get-MaxReqId
function Get-Details($session, $id) {
    Invoke-WebRequest "$BaseUrl/Requests/Details/$id" -WebSession $session -UseBasicParsing -SkipHttpErrorCheck
}
# รายการ action ที่ "มีปุ่มให้กด" บนหน้านั้นจริง ๆ
function Get-Actions($page) {
    ([regex]::Matches($page.Content, 'name="statusAction" value="(\w+)"')).ForEach({ $_.Groups[1].Value }) | Sort-Object -Unique
}
function Invoke-Status($session, $id, $action, $reason = '') {
    $page = Get-Details $session $id
    Invoke-WebRequest "$BaseUrl/Requests/ChangeStatus" -Method Post -WebSession $session -UseBasicParsing -SkipHttpErrorCheck -Body @{
        '__RequestVerificationToken' = (Get-Token $page)
        id = $id; statusAction = $action; reason = $reason; returnTo = 'details' }
}
function Get-Status($page) {
    if ($page.Content -match '(?s)<h1[^>]*>\s*(MSG-[A-Z]{3}-\d{4}-\d{4})\s*</h1>\s*<span class="badge[^"]*">([^<]+)</span>') { return $Matches[2].Trim() }
    return '(อ่านสถานะไม่ได้)'
}

$u = New-Session '10002'   # ผู้แจ้ง
$m = New-Session '10003'   # Messenger SDC

$sendDate = (Get-Date).AddDays(1)
while ($sendDate.DayOfWeek -in 'Saturday','Sunday') { $sendDate = $sendDate.AddDays(1) }
$ds = $sendDate.ToString('yyyy-MM-dd')
Write-Host ''
Write-Host "=== เตรียมข้อมูล : สร้าง 3 ใบ วันที่ส่ง $($sendDate.ToString('dd/MM/yyyy')) ===" -ForegroundColor Cyan

$ids = @(); $nos = @()
foreach ($n in 1..3) {
    $page = Invoke-WebRequest "$BaseUrl/Requests/Create" -WebSession $u -UseBasicParsing
    $b = @{
        '__RequestVerificationToken' = (Get-Token $page)
        'RequesterEmpCode' = '10002'; 'SendDate' = $ds
        'ContactName' = "ปลายทางทดสอบหมวด 3 ใบที่ $n"
        'Address' = "$n ถนนทดสอบ"; 'Detail' = "ใบงานทดสอบ state machine ใบที่ $n"; 'IsPersonal' = 'false'
    }
    $codes = 'SendDoc','ReceiveDoc','ReceiveCheck','PlaceBill','RenewTax','Other'
    for ($i=0; $i -lt 6; $i++) { $b["JobTypes[$i].Code"]=$codes[$i]; $b["JobTypes[$i].Selected"]= if($i -eq 0){'true'}else{'false'} }
    $r = Invoke-WebRequest "$BaseUrl/Requests/Create" -Method Post -WebSession $u -UseBasicParsing -Body $b
    $ids += ($r.BaseResponse.RequestMessage.RequestUri.AbsolutePath -replace '.*/Requests/Details/','')
    $nos += $(if ($r.Content -match 'MSG-[A-Z]{3}-\d{4}-\d{4}') { $Matches[0] })
}
Write-Host ("   สร้างแล้ว : " + ($nos -join ', ')) -ForegroundColor DarkGray

# ---------------------------------------------------------------- 3.1
Write-Host ''
Write-Host '=== 3.1 Messenger เปิดคิวงานประจำวัน ===' -ForegroundColor Cyan
$queue = Invoke-WebRequest "$BaseUrl/Queue?date=$ds" -WebSession $m -UseBasicParsing -SkipHttpErrorCheck
Assert 'เปิดหน้าคิวงานได้' ($queue.StatusCode -eq 200) "(ได้ $($queue.StatusCode))"
Assert 'มีกลุ่ม "รอยืนยันรับงาน"' ($queue.Content -match 'รอยืนยันรับงาน')
Assert 'มีกลุ่ม "คิววิ่งงาน"'    ($queue.Content -match 'คิววิ่งงาน')
Assert 'มีกลุ่ม "ปิดแล้ววันนี้"'  ($queue.Content -match 'ปิดแล้ววันนี้')
Assert 'ใบที่เพิ่งสร้างอยู่ในคิว' ($queue.Content -match [regex]::Escape($nos[0]))

# ---------------------------------------------------------------- 3.2
Write-Host ''
Write-Host '=== 3.2 ยืนยันรับงาน 3 ใบติดกัน (D11/D21) ===' -ForegroundColor Cyan
foreach ($i in 0..2) {
    Invoke-Status $m $ids[$i] 'Confirm' | Out-Null
    $d = Get-Details $m $ids[$i]
    $seq = if ($d.Content -match 'ลำดับที่\s*(\d+)') { [int]$Matches[1] } else { -1 }
    Assert "$($nos[$i]) : สถานะ 'กำลังส่ง' และได้ลำดับ $($i+1)" ((Get-Status $d) -eq 'กำลังส่ง' -and $seq -eq ($i+1)) "(สถานะ $(Get-Status $d) ลำดับ $seq)"
}

# ---------------------------------------------------------------- 3.3
Write-Host ''
Write-Host '=== 3.3 กดลูกศรขึ้นที่ใบลำดับ 2 (D21) ===' -ForegroundColor Cyan
$queue = Invoke-WebRequest "$BaseUrl/Queue?date=$ds" -WebSession $m -UseBasicParsing
Invoke-WebRequest "$BaseUrl/Queue/Move" -Method Post -WebSession $m -UseBasicParsing -SkipHttpErrorCheck -Body @{
    '__RequestVerificationToken' = (Get-Token $queue); id = $ids[1]; direction = 'Up'; date = $ds } | Out-Null
$s1 = if ((Get-Details $m $ids[0]).Content -match 'ลำดับที่\s*(\d+)') { [int]$Matches[1] }
$s2 = if ((Get-Details $m $ids[1]).Content -match 'ลำดับที่\s*(\d+)') { [int]$Matches[1] }
Assert "ใบที่ 2 ขึ้นมาเป็นลำดับ 1 (ได้ $s2)" ($s2 -eq 1)
Assert "ใบที่ 1 ถูกสลับลงไปเป็นลำดับ 2 (ได้ $s1)" ($s1 -eq 2)

# ---------------------------------------------------------------- 3.4 / 3.5
Write-Host ''
Write-Host '=== 3.4 / 3.5 พักการส่ง (§6 ต้องมีเหตุผล) ===' -ForegroundColor Cyan
Invoke-Status $m $ids[0] 'Pause' '' | Out-Null
$d = Get-Details $m $ids[0]
Assert '3.4 พักโดยไม่กรอกเหตุผล — สถานะไม่เปลี่ยน' ((Get-Status $d) -eq 'กำลังส่ง') "(ได้ $(Get-Status $d))"

$reason = "รถเสียระหว่างทาง ทดสอบ $(Get-Date -Format 'HH:mm:ss')"
Invoke-Status $m $ids[0] 'Pause' $reason | Out-Null
$d = Get-Details $m $ids[0]
Assert '3.5 พักพร้อมเหตุผล — สถานะเป็น "พักการส่ง"' ((Get-Status $d) -eq 'พักการส่ง') "(ได้ $(Get-Status $d))"
Assert '3.5 เหตุผลไปโผล่ในประวัติสถานะ' ($d.Content -match [regex]::Escape($reason))

# ---------------------------------------------------------------- 3.6
Write-Host ''
Write-Host '=== 3.6 ใบที่พักอยู่ ต้องไม่มีปุ่มปิดงาน ===' -ForegroundColor Cyan
$acts = Get-Actions $d
Assert ("ปุ่มที่มีให้กดคือ : " + ($acts -join ', ')) ($acts.Count -gt 0)
Assert 'ไม่มีปุ่ม "ปิดงาน" (Complete)' ($acts -notcontains 'Complete')
Assert 'มีปุ่ม "กลับมาส่งต่อ" (Resume)' ($acts -contains 'Resume')

# ---------------------------------------------------------------- 3.7
Write-Host ''
Write-Host '=== 3.7 ผู้แจ้งเปิดใบที่ Messenger รับไปแล้ว (BR-2 + D7) ===' -ForegroundColor Cyan
$owner = Get-Details $u $ids[2]
Assert 'ผู้แจ้งยังเปิดดูใบตัวเองได้' ($owner.StatusCode -eq 200)
Assert 'แต่ไม่มีปุ่มเปลี่ยนสถานะใด ๆ เหลือ (รวมยกเลิก)' ((Get-Actions $owner).Count -eq 0) "(เจอ $((Get-Actions $owner) -join ', '))"
$edit = Invoke-WebRequest "$BaseUrl/Requests/Edit/$($ids[2])" -WebSession $u -UseBasicParsing -SkipHttpErrorCheck
Assert 'เปิดฟอร์มแก้ไขไม่ได้แล้ว' ($edit.StatusCode -ne 200 -or $edit.BaseResponse.RequestMessage.RequestUri.AbsolutePath -notmatch '/Requests/Edit') "(ได้ $($edit.StatusCode))"

# ---------------------------------------------------------------- 3.8
Write-Host ''
Write-Host '=== 3.8 ผู้แจ้งยกเลิกใบตัวเองตอน "รับแจ้ง" (D7) ===' -ForegroundColor Cyan
$page = Invoke-WebRequest "$BaseUrl/Requests/Create" -WebSession $u -UseBasicParsing
$b = @{ '__RequestVerificationToken' = (Get-Token $page); 'RequesterEmpCode'='10002'; 'SendDate'=$ds
        'ContactName'='ปลายทางทดสอบ 3.8'; 'Address'='8 ถนนทดสอบ'; 'Detail'='ใบสำหรับทดสอบการยกเลิก'; 'IsPersonal'='false' }
$codes = 'SendDoc','ReceiveDoc','ReceiveCheck','PlaceBill','RenewTax','Other'
for ($i=0; $i -lt 6; $i++) { $b["JobTypes[$i].Code"]=$codes[$i]; $b["JobTypes[$i].Selected"]= if($i -eq 0){'true'}else{'false'} }
$r = Invoke-WebRequest "$BaseUrl/Requests/Create" -Method Post -WebSession $u -UseBasicParsing -Body $b
$cancelId = $r.BaseResponse.RequestMessage.RequestUri.AbsolutePath -replace '.*/Requests/Details/',''
$own = Get-Details $u $cancelId
Assert 'ตอนสถานะ "รับแจ้ง" ผู้แจ้งมีปุ่มยกเลิก' ((Get-Actions $own) -contains 'Cancel')
Invoke-Status $u $cancelId 'Cancel' 'ทดสอบ D7 ผู้แจ้งยกเลิกเอง' | Out-Null
$own = Get-Details $u $cancelId
Assert 'ยกเลิกได้จริง' ((Get-Status $own) -eq 'ยกเลิก') "(ได้ $(Get-Status $own))"

# ---------------------------------------------------------------- 3.9
Write-Host ''
Write-Host '=== 3.9 ใบที่ปิดไปแล้ว ต้องไม่มีปุ่มเหลือ ===' -ForegroundColor Cyan
Invoke-Status $m $ids[1] 'Complete' | Out-Null
$done = Get-Details $m $ids[1]
Assert 'ปิดงานสำเร็จ' ((Get-Status $done) -eq 'เสร็จงานแล้ว') "(ได้ $(Get-Status $done))"
Assert 'Messenger เปิดดูแล้วไม่มีปุ่มเปลี่ยนสถานะเหลือเลย' ((Get-Actions $done).Count -eq 0) "(เจอ $((Get-Actions $done) -join ', '))"
$cancelled = Get-Details $u $cancelId
Assert 'ใบที่ยกเลิกแล้วก็ไม่มีปุ่มเหลือเช่นกัน' ((Get-Actions $cancelled).Count -eq 0) "(เจอ $((Get-Actions $cancelled) -join ', '))"

# ---------------------------------------------------------------- 3.10
Write-Host ''
Write-Host '=== 3.10 ประวัติสถานะ ===' -ForegroundColor Cyan
$hist = Get-Details $m $ids[0]
foreach ($want in 'รับแจ้ง','กำลังส่ง','พักการส่ง') {
    Assert "ประวัติมีสถานะ '$want'" ($hist.Content -match [regex]::Escape($want))
}
Assert 'ประวัติระบุชื่อผู้ทำ (ประเสริฐ = Messenger)' ($hist.Content -match 'ประเสริฐ')
Assert 'ประวัติระบุรหัสพนักงานคู่กับชื่อ' ($hist.Content -match '10003')
Assert 'ประวัติมีวันเวลาแบบ dd/MM/yyyy HH:mm' ($hist.Content -match '\d{2}/\d{2}/\d{4}\s+\d{2}:\d{2}')

# ---------------------------------------------------------------- 3.11
Write-Host ''
Write-Host '=== 3.11 ค้นด้วยช่วงวันที่บันทึก + สถานะ ===' -ForegroundColor Cyan
$today = (Get-Date).ToString('yyyy-MM-dd')
$f1 = Invoke-WebRequest "$BaseUrl/Requests?requestDateFrom=$today&requestDateTo=$today&status=Cancelled" -WebSession $m -UseBasicParsing
$cancelNo = if ($cancelled.Content -match 'MSG-[A-Z]{3}-\d{4}-\d{4}') { $Matches[0] }
Assert "กรองสถานะ 'ยกเลิก' แล้วเจอใบที่เพิ่งยกเลิก ($cancelNo)" ($f1.Content -match [regex]::Escape($cancelNo))
Assert 'และไม่มีใบที่ปิดงานไปปนอยู่' ($f1.Content -notmatch [regex]::Escape($nos[1]))

$f2 = Invoke-WebRequest "$BaseUrl/Requests?requestDateFrom=$today&requestDateTo=$today&status=Completed" -WebSession $m -UseBasicParsing
Assert "กรองสถานะ 'เสร็จงานแล้ว' แล้วเจอใบที่เพิ่งปิด ($($nos[1]))" ($f2.Content -match [regex]::Escape($nos[1]))
Assert 'และไม่มีใบที่ยกเลิกปนอยู่' ($f2.Content -notmatch [regex]::Escape($cancelNo))

$past = (Get-Date).AddDays(-60).ToString('yyyy-MM-dd')
$f3 = Invoke-WebRequest "$BaseUrl/Requests?requestDateFrom=$past&requestDateTo=$past" -WebSession $m -UseBasicParsing
Assert 'กรองช่วงวันที่ที่ไม่มีงาน แล้วไม่เจอใบของวันนี้' ($f3.Content -notmatch [regex]::Escape($nos[1]))

exit (Complete-TestRun $sinceReqId -KeepData:$KeepData)
