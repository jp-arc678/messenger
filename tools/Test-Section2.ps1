<#
    ทดสอบ UAT หมวด 2 — แจ้งงาน (Phase 1 · BR-1, BR-8, D15-D18)
    ครอบคลุม 2.1, 2.2, 2.3, 2.6, 2.7, 2.8, 2.9, 2.10, 2.11
    (2.4 ทดสอบไม่ได้ที่นี่ — ต้องแจ้งงานจริงก่อน 10:00 น. ดู IClock/SystemClock)

    ใช้:  pwsh -File tools\Test-Section2.ps1
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

# วันทำการถัดไปที่เลือกได้ (ไม่ย้อนหลัง ไม่เสาร์-อาทิตย์)
$workday = Get-Date
while ($workday.DayOfWeek -in 'Saturday','Sunday') { $workday = $workday.AddDays(1) }

function New-CreateBody($session, [hashtable] $overrides = @{}) {
    $page = Invoke-WebRequest "$BaseUrl/Requests/Create" -WebSession $session -UseBasicParsing
    $b = @{
        '__RequestVerificationToken' = (Get-Token $page)
        'RequesterEmpCode' = '10002'
        'SendDate'         = $workday.ToString('yyyy-MM-dd')
        'ContactName'      = 'บริษัท ทดสอบหมวดสอง จำกัด'
        'Phone'            = '021234567'
        'Address'          = '9 ถนนทดสอบ แขวงทดสอบ'
        'Detail'           = 'ใบงานสำหรับทดสอบ UAT หมวด 2'
        'IsPersonal'       = 'false'
    }
    $codes = 'SendDoc','ReceiveDoc','ReceiveCheck','PlaceBill','RenewTax','Other'
    for ($i = 0; $i -lt 6; $i++) {
        $b["JobTypes[$i].Code"] = $codes[$i]
        $b["JobTypes[$i].Selected"] = if ($i -eq 0) { 'true' } else { 'false' }
    }
    foreach ($k in $overrides.Keys) { $b[$k] = $overrides[$k] }
    return $b
}
function Submit-Create($session, $body) {
    Invoke-WebRequest "$BaseUrl/Requests/Create" -Method Post -WebSession $session `
        -UseBasicParsing -Body $body -SkipHttpErrorCheck
}
function Get-SavedId($resp) {
    if ($resp.BaseResponse.RequestMessage.RequestUri.AbsolutePath -match '/Requests/Details/(\d+)') { return $Matches[1] }
    return $null
}

$u10002 = New-Session '10002'   # U-User SDC (เจ้าของใบงาน)
$u10004 = New-Session '10004'   # U-User SDC (คนละคน)

# ---------------------------------------------------------------- 2.1
Write-Host ''
Write-Host '=== 2.1 สร้างใบงานกรอกครบ ติ๊ก "ส่งเอกสาร" ===' -ForegroundColor Cyan
$r = Submit-Create $u10002 (New-CreateBody $u10002)
$mainId = Get-SavedId $r
$reqNo = if ($r.Content -match 'MSG-[A-Z]{3}-\d{4}-\d{4}') { $Matches[0] } else { '(ไม่พบ)' }
Assert 'บันทึกได้ (redirect ไปหน้ารายละเอียด)' ($null -ne $mainId) "(uri = $($r.BaseResponse.RequestMessage.RequestUri.AbsolutePath))"
$expectPrefix = 'MSG-SDC-' + (Get-Date).ToString('yyMM') + '-'
Assert "เลขใบงานถูกรูปแบบ BR-8 : $reqNo" ($reqNo -like "$expectPrefix*") "(คาดว่าขึ้นต้น $expectPrefix)"

# ---------------------------------------------------------------- 2.2
Write-Host ''
Write-Host '=== 2.2 ไม่ติ๊กประเภทงานเลย (D18) ===' -ForegroundColor Cyan
$body = New-CreateBody $u10002
for ($i = 0; $i -lt 6; $i++) { $body["JobTypes[$i].Selected"] = 'false' }
$r = Submit-Create $u10002 $body
Assert 'บันทึกไม่ได้' ($null -eq (Get-SavedId $r))
Assert 'ขึ้นข้อความ "กรุณาเลือกประเภทงานอย่างน้อย 1 ประเภท"' ($r.Content -match 'กรุณาเลือกประเภทงานอย่างน้อย 1 ประเภท')

# ---------------------------------------------------------------- 2.3
Write-Host ''
Write-Host '=== 2.3 เว้นว่างช่องบังคับ (D15) ===' -ForegroundColor Cyan
$r = Submit-Create $u10002 (New-CreateBody $u10002 @{ ContactName=''; Address=''; Detail=''; Phone='' })
Assert 'บันทึกไม่ได้' ($null -eq (Get-SavedId $r))
Assert 'ขึ้นข้อความเรื่องผู้ติดต่อ'   ($r.Content -match 'กรุณาระบุชื่อผู้ติดต่อ')
Assert 'ขึ้นข้อความเรื่องที่อยู่'     ($r.Content -match 'กรุณาระบุที่อยู่')
Assert 'ขึ้นข้อความเรื่องรายละเอียด'  ($r.Content -match 'กรุณาระบุรายละเอียดงาน')
# เบอร์โทรไม่บังคับ
$r = Submit-Create $u10002 (New-CreateBody $u10002 @{ Phone=''; Detail='ใบงานทดสอบ 2.3 เบอร์โทรเว้นว่าง' })
Assert 'เว้นเบอร์โทรอย่างเดียว บันทึกได้ตามปกติ' ($null -ne (Get-SavedId $r))

# ---------------------------------------------------------------- 2.6 / 2.7
Write-Host ''
Write-Host '=== 2.6 / 2.7 วันที่ส่งที่เลือกเอง (D16) ===' -ForegroundColor Cyan
$r = Submit-Create $u10002 (New-CreateBody $u10002 @{ SendDate=(Get-Date).AddDays(-1).ToString('yyyy-MM-dd') })
Assert '2.6 เลือกวันเมื่อวาน — บันทึกไม่ได้' ($null -eq (Get-SavedId $r))
Assert '2.6 ขึ้นข้อความ "วันที่ส่งต้องไม่เป็นวันย้อนหลัง"' ($r.Content -match 'วันที่ส่งต้องไม่เป็นวันย้อนหลัง')

$sat = Get-Date; while ($sat.DayOfWeek -ne 'Saturday') { $sat = $sat.AddDays(1) }
$sun = $sat.AddDays(1)
$r = Submit-Create $u10002 (New-CreateBody $u10002 @{ SendDate=$sat.ToString('yyyy-MM-dd') })
Assert "2.7 เลือกวันเสาร์ ($($sat.ToString('dd/MM/yyyy'))) — บันทึกไม่ได้" ($null -eq (Get-SavedId $r))
Assert '2.7 ขึ้นข้อความเรื่องเสาร์-อาทิตย์' ($r.Content -match 'วันที่ส่งต้องไม่ตรงกับวันเสาร์หรือวันอาทิตย์')
$r = Submit-Create $u10002 (New-CreateBody $u10002 @{ SendDate=$sun.ToString('yyyy-MM-dd') })
Assert "2.7 เลือกวันอาทิตย์ ($($sun.ToString('dd/MM/yyyy'))) — บันทึกไม่ได้" ($null -eq (Get-SavedId $r))

# ---------------------------------------------------------------- 2.9
Write-Host ''
Write-Host '=== 2.9 ตัวเลือก "ผู้แจ้ง" ในฟอร์ม (BR-6) ===' -ForegroundColor Cyan
$page = Invoke-WebRequest "$BaseUrl/Requests/Create" -WebSession $u10002 -UseBasicParsing
if ($page.Content -match '(?s)id="RequesterEmpCode".*?</select>') {
    $select = $Matches[0]
    $opts = ([regex]::Matches($select, 'value="(\d+)"')).ForEach({ $_.Groups[1].Value }) | Sort-Object -Unique
    $sbkOpts = $opts | Where-Object { $_ -like '2*' }
    Assert "มีตัวเลือกพนักงาน SDC ครบ ($($opts -join ', '))" ($opts.Count -ge 4)
    Assert 'ไม่มีพนักงานสาขา SBK ปนอยู่เลย' ($sbkOpts.Count -eq 0) "(เจอ $($sbkOpts -join ', '))"
} else { Assert 'หา dropdown ผู้แจ้งเจอ' $false }

# ---------------------------------------------------------------- 2.8
Write-Host ''
Write-Host '=== 2.8 แจ้งงานแทนคนอื่น (D17) ===' -ForegroundColor Cyan
$r = Submit-Create $u10002 (New-CreateBody $u10002 @{ RequesterEmpCode='10004'; Detail='ใบงานทดสอบ 2.8 แจ้งแทนคนอื่น' })
$proxyId = Get-SavedId $r
Assert 'บันทึกได้' ($null -ne $proxyId)
Assert 'หน้ารายละเอียดขึ้นผู้แจ้ง = อารีย์ พากเพียร' ($r.Content -match 'อารีย์')
Assert 'หน้ารายละเอียดระบุว่าบันทึกโดย สมหญิง (10002)' ($r.Content -match 'สมหญิง')
# D37 — คนกรอกต้องยังเปิดดูใบที่ตัวเองแจ้งแทนได้
Assert 'D37 คนกรอก (10002) ยังเปิดดูใบนี้ได้' ($r.StatusCode -eq 200)

# ---------------------------------------------------------------- 2.10
Write-Host ''
Write-Host '=== 2.10 แก้ใบงานตัวเองตอนสถานะ "รับแจ้ง" (BR-2) ===' -ForegroundColor Cyan
$editPage = Invoke-WebRequest "$BaseUrl/Requests/Edit/$mainId" -WebSession $u10002 -UseBasicParsing -SkipHttpErrorCheck
Assert 'เปิดฟอร์มแก้ไขได้' ($editPage.StatusCode -eq 200) "(ได้ $($editPage.StatusCode))"
$rowVersion = if ($editPage.Content -match 'name="RowVersion" value="([^"]*)"') { $Matches[1] } else { '' }
$newDetail = "แก้ไขแล้วเมื่อ $(Get-Date -Format 'HH:mm:ss') โดย UAT 2.10"
$eb = New-CreateBody $u10002 @{ ReqId=$mainId; RowVersion=$rowVersion; Detail=$newDetail }
$eb['__RequestVerificationToken'] = (Get-Token $editPage)
$r = Invoke-WebRequest "$BaseUrl/Requests/Edit/$mainId" -Method Post -WebSession $u10002 `
        -UseBasicParsing -Body $eb -SkipHttpErrorCheck
Assert 'บันทึกการแก้ไขได้' ($null -ne (Get-SavedId $r))
$after = Invoke-WebRequest "$BaseUrl/Requests/Details/$mainId" -WebSession $u10002 -UseBasicParsing -SkipHttpErrorCheck
Assert 'ข้อความใหม่ขึ้นในหน้ารายละเอียดจริง' ($after.Content -match [regex]::Escape($newDetail))

# ---------------------------------------------------------------- 2.11
Write-Host ''
Write-Host '=== 2.11 คนอื่นในสาขาเดียวกันเปิดใบเราไม่ได้ (D37) ===' -ForegroundColor Cyan
$r = Invoke-WebRequest "$BaseUrl/Requests/Details/$mainId" -WebSession $u10004 -UseBasicParsing -SkipHttpErrorCheck
Assert '10004 เปิดใบงานของ 10002 ไม่ได้ (404)' ($r.StatusCode -eq 404) "(ได้ $($r.StatusCode))"

exit (Complete-TestRun $sinceReqId -KeepData:$KeepData)
