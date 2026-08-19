<#
    ทดสอบ UAT หมวด 4 — รูปยืนยัน + ปิดงาน (Phase 3 · BR-3, BR-4, D23-D25)
    ครอบคลุม 4.1-4.4, 4.6-4.10
    (4.5 ทดสอบที่นี่ไม่ได้ — การย่อรูปเกิดด้วย JavaScript ในเบราว์เซอร์
     การยิง HTTP ตรงจะข้ามขั้นตอนนั้นทั้งหมด)

    ใช้:  pwsh -File tools\Test-Section4.ps1
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

# ไฟล์ชั่วคราวของเทสต์ (รูป/ไฟล์ export) — ไม่เขียนลงโฟลเดอร์โปรเจกต์
$tmp = [IO.Path]::GetTempPath()
function Get-Details($s, $id) { Invoke-WebRequest "$BaseUrl/Requests/Details/$id" -WebSession $s -UseBasicParsing -SkipHttpErrorCheck }
function Get-Status($p) {
    if ($p.Content -match '(?s)<h1[^>]*>\s*(MSG-[A-Z]{3}-\d{4}-\d{4})\s*</h1>\s*<span class="badge[^"]*">([^<]+)</span>') { return $Matches[2].Trim() }
    return '(อ่านไม่ได้)'
}
function Invoke-Status($s, $id, $action, $reason = '') {
    $p = Get-Details $s $id
    Invoke-WebRequest "$BaseUrl/Requests/ChangeStatus" -Method Post -WebSession $s -UseBasicParsing -SkipHttpErrorCheck -Body @{
        '__RequestVerificationToken' = (Get-Token $p); id = $id; statusAction = $action; reason = $reason; returnTo = 'details' }
}
function New-Request($s, [string[]] $jobTypes, $sendDate) {
    $p = Invoke-WebRequest "$BaseUrl/Requests/Create" -WebSession $s -UseBasicParsing
    $b = @{ '__RequestVerificationToken' = (Get-Token $p); 'RequesterEmpCode'='10002'
            'SendDate'=$sendDate; 'ContactName'='ปลายทางทดสอบหมวด 4'
            'Address'='4 ถนนทดสอบ'; 'Detail'='ใบงานทดสอบรูปและ BR-4'; 'IsPersonal'='false' }
    $codes = 'SendDoc','ReceiveDoc','ReceiveCheck','PlaceBill','RenewTax','Other'
    for ($i=0; $i -lt 6; $i++) {
        $b["JobTypes[$i].Code"] = $codes[$i]
        $b["JobTypes[$i].Selected"] = if ($jobTypes -contains $codes[$i]) { 'true' } else { 'false' }
    }
    $r = Invoke-WebRequest "$BaseUrl/Requests/Create" -Method Post -WebSession $s -UseBasicParsing -Body $b
    return [pscustomobject]@{
        Id = ($r.BaseResponse.RequestMessage.RequestUri.AbsolutePath -replace '.*/Requests/Details/','')
        ReqNo = $(if ($r.Content -match 'MSG-[A-Z]{3}-\d{4}-\d{4}') { $Matches[0] })
    }
}
function Send-Photo($s, $reqId, $filePath, $photoType = 'send') {
    $p = Get-Details $s $reqId
    Invoke-WebRequest "$BaseUrl/Photos/Upload" -Method Post -WebSession $s -UseBasicParsing -SkipHttpErrorCheck -Form @{
        '__RequestVerificationToken' = (Get-Token $p)
        reqId = $reqId; photoType = $photoType; file = Get-Item $filePath }
}

# ---- เตรียมไฟล์ทดสอบ ----
Add-Type -AssemblyName System.Drawing
$png = Join-Path $tmp 'uat-photo.png'
$bmp = New-Object System.Drawing.Bitmap 640, 480
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::SteelBlue)
$g.DrawString('UAT 4', (New-Object System.Drawing.Font('Arial', 40)), [System.Drawing.Brushes]::White, 40, 200)
$g.Dispose(); $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

# ไฟล์ PDF ที่เปลี่ยนนามสกุลเป็น .jpg (สำหรับ 4.6)
$fake = Join-Path $tmp 'not-really-a-photo.jpg'
[IO.File]::WriteAllBytes($fake, [Text.Encoding]::ASCII.GetBytes("%PDF-1.4`n1 0 obj<</Type/Catalog>>endobj`n%%EOF"))

Write-Host ("   ไฟล์ทดสอบ : PNG {0:N0} bytes · ไฟล์ปลอม {1:N0} bytes" -f (Get-Item $png).Length, (Get-Item $fake).Length) -ForegroundColor DarkGray

$u = New-Session '10002'   # ผู้แจ้ง
$m = New-Session '10003'   # Messenger SDC
$sbk = New-Session '20003' # Messenger SBK (คนละสาขา)

$sd = (Get-Date).AddDays(1)
while ($sd.DayOfWeek -in 'Saturday','Sunday') { $sd = $sd.AddDays(1) }
$ds = $sd.ToString('yyyy-MM-dd')

# ---------------------------------------------------------------- 4.7
Write-Host ''
Write-Host '=== 4.7 ใบสถานะ "รับแจ้ง" ยังไม่มีฟอร์มอัปโหลด (D23) ===' -ForegroundColor Cyan
$a = New-Request $u @('SendDoc','ReceiveDoc') $ds
$d = Get-Details $m $a.Id
Assert "$($a.ReqNo) สถานะ 'รับแจ้ง'" ((Get-Status $d) -eq 'รับแจ้ง') "(ได้ $(Get-Status $d))"
Assert 'ยังไม่มีฟอร์มอัปโหลดรูป' ($d.Content -notmatch 'id="photoUploadForm"')
$blocked = Send-Photo $m $a.Id $png
Assert 'และยิงอัปโหลดตรง ๆ ก็ไม่ผ่าน' ((Get-Details $m $a.Id).Content -notmatch '/Photos/Show/')

# ---------------------------------------------------------------- 4.1
Write-Host ''
Write-Host '=== 4.1 ยืนยันรับงานใบที่มี "รับเอกสาร" (BR-4) ===' -ForegroundColor Cyan
Invoke-Status $m $a.Id 'Confirm' | Out-Null
$d = Get-Details $m $a.Id
Assert 'สถานะเป็น "กำลังส่ง"' ((Get-Status $d) -eq 'กำลังส่ง') "(ได้ $(Get-Status $d))"
Assert 'ขึ้นแถบเตือนว่าต้องยืนยันรับของก่อนปิดงาน' ($d.Content -match 'ต้องยืนยันว่ารับของแล้วก่อนจึงปิดงานได้')
Assert 'มีปุ่ม "ยืนยันรับของแล้ว"' ($d.Content -match 'ยืนยันรับของแล้ว')
Assert 'ตอนนี้มีฟอร์มอัปโหลดรูปแล้ว (D23)' ($d.Content -match 'id="photoUploadForm"')

# ---------------------------------------------------------------- 4.2
Write-Host ''
Write-Host '=== 4.2 กดปิดงานทันทีโดยยังไม่ยืนยันรับของ ===' -ForegroundColor Cyan
Invoke-Status $m $a.Id 'Complete' | Out-Null
$d = Get-Details $m $a.Id
Assert 'ปิดไม่ได้ — สถานะยังเป็น "กำลังส่ง"' ((Get-Status $d) -eq 'กำลังส่ง') "(ได้ $(Get-Status $d))"

# ---------------------------------------------------------------- 4.6
Write-Host ''
Write-Host '=== 4.6 อัปไฟล์ที่ไม่ใช่รูป (PDF เปลี่ยนนามสกุลเป็น .jpg) ===' -ForegroundColor Cyan
$r = Send-Photo $m $a.Id $fake
Assert 'ถูกปฏิเสธด้วยข้อความเรื่องชนิดไฟล์' ($r.Content -match 'รองรับเฉพาะไฟล์รูปแบบ JPG และ PNG')
$d = Get-Details $m $a.Id
Assert 'ไม่มีรูปถูกบันทึกลงระบบ' ($d.Content -notmatch '/Photos/Show/')

# ---- อัปรูปจริง 1 ใบไว้ใช้ต่อ ----
Write-Host ''
Write-Host '=== อัปโหลดรูปจริง (PNG 640x480) ===' -ForegroundColor Cyan
Send-Photo $m $a.Id $png 'receive' | Out-Null
$d = Get-Details $m $a.Id
$photoId = if ($d.Content -match '/Photos/Show/(\d+)') { $Matches[1] }
Assert "อัปโหลดสำเร็จ ได้ photoId = $photoId" ($null -ne $photoId)
Assert 'มีปุ่มลบรูป (D24)' ($d.Content -match 'ลบรูป')

# ---------------------------------------------------------------- 4.9
Write-Host ''
Write-Host '=== 4.9 ผู้แจ้งเปิดใบตัวเองที่มีรูป ===' -ForegroundColor Cyan
$own = Get-Details $u $a.Id
Assert 'ผู้แจ้งเห็นรูป' ($own.Content -match '/Photos/Show/')
Assert 'แต่ไม่มีฟอร์มอัปโหลด' ($own.Content -notmatch 'id="photoUploadForm"')
Assert 'และไม่มีปุ่มลบรูป' ($own.Content -notmatch 'ลบรูป')
$showOwn = Invoke-WebRequest "$BaseUrl/Photos/Show/$photoId" -WebSession $u -UseBasicParsing -SkipHttpErrorCheck
Assert 'ผู้แจ้งเปิดไฟล์รูปได้จริง' ($showOwn.StatusCode -eq 200) "(ได้ $($showOwn.StatusCode))"

# ---------------------------------------------------------------- 4.10
Write-Host ''
Write-Host '=== 4.10 คนสาขาอื่นเปิด URL รูป (BR-6 · D25) ===' -ForegroundColor Cyan
$showSbk = Invoke-WebRequest "$BaseUrl/Photos/Show/$photoId" -WebSession $sbk -UseBasicParsing -SkipHttpErrorCheck
Assert '20003 (SBK) เปิดรูปของ SDC ไม่ได้ (404)' ($showSbk.StatusCode -eq 404) "(ได้ $($showSbk.StatusCode))"

# ---------------------------------------------------------------- 4.3
Write-Host ''
Write-Host '=== 4.3 กดยืนยันรับของ แล้วปิดงาน ===' -ForegroundColor Cyan
$d = Get-Details $m $a.Id
Invoke-WebRequest "$BaseUrl/Requests/ConfirmReceipt" -Method Post -WebSession $m -UseBasicParsing -SkipHttpErrorCheck -Body @{
    '__RequestVerificationToken' = (Get-Token $d); id = $a.Id; returnTo = 'details' } | Out-Null
$d = Get-Details $m $a.Id
Assert 'ยืนยันรับของแล้ว' ($d.Content -match 'ยืนยันแล้ว' -or $d.Content -notmatch 'ต้องยืนยันว่ารับของแล้วก่อนจึงปิดงานได้')
Invoke-Status $m $a.Id 'Complete' | Out-Null
$d = Get-Details $m $a.Id
Assert 'ปิดงานได้แล้ว' ((Get-Status $d) -eq 'เสร็จงานแล้ว') "(ได้ $(Get-Status $d))"

# ---------------------------------------------------------------- 4.8
Write-Host ''
Write-Host '=== 4.8 หลังปิดงาน ฟอร์ม/ปุ่มลบต้องหาย (D23/D24) ===' -ForegroundColor Cyan
Assert 'ไม่มีฟอร์มอัปโหลดแล้ว' ($d.Content -notmatch 'id="photoUploadForm"')
Assert 'ไม่มีปุ่มลบรูปแล้ว' ($d.Content -notmatch 'ลบรูป')
Assert 'แต่ยังเห็นรูปเดิมอยู่' ($d.Content -match '/Photos/Show/')
$delAfter = Invoke-WebRequest "$BaseUrl/Photos/Delete" -Method Post -WebSession $m -UseBasicParsing -SkipHttpErrorCheck -Body @{
    '__RequestVerificationToken' = (Get-Token $d); id = $photoId; reqId = $a.Id }
$stillThere = Invoke-WebRequest "$BaseUrl/Photos/Show/$photoId" -WebSession $m -UseBasicParsing -SkipHttpErrorCheck
Assert 'ยิงลบตรง ๆ หลังปิดงานก็ไม่สำเร็จ' ($stillThere.StatusCode -eq 200) "(ได้ $($stillThere.StatusCode))"

# ---------------------------------------------------------------- 4.4
Write-Host ''
Write-Host '=== 4.4 ใบที่ไม่มี "รับเอกสาร" ปิดได้เลย ===' -ForegroundColor Cyan
$b = New-Request $u @('SendDoc') $ds
Invoke-Status $m $b.Id 'Confirm' | Out-Null
$d = Get-Details $m $b.Id
Assert 'ไม่มีแถบเตือน BR-4' ($d.Content -notmatch 'ต้องยืนยันว่ารับของแล้วก่อนจึงปิดงานได้')
Assert 'ไม่มีปุ่ม "ยืนยันรับของแล้ว"' ($d.Content -notmatch 'ยืนยันรับของแล้ว')
Invoke-Status $m $b.Id 'Complete' | Out-Null
$d = Get-Details $m $b.Id
Assert "$($b.ReqNo) ปิดงานได้เลยโดยไม่ต้องยืนยันรับของ" ((Get-Status $d) -eq 'เสร็จงานแล้ว') "(ได้ $(Get-Status $d))"

exit (Complete-TestRun $sinceReqId -KeepData:$KeepData)
