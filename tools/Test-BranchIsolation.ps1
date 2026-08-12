<#
    Test-BranchIsolation.ps1
    ทดสอบว่าการแยกข้อมูลตามสาขา (BR-6) และสิทธิ์ตาม role (§5) รัดกุมจริง
    โดย "ยิงตรง" ทุก endpoint ด้วยผู้ใช้ที่ไม่ควรมีสิทธิ์ แล้วยืนยันว่าถูกปฏิเสธทุกทาง

    วิธีคิด : หน้าจอซ่อนปุ่มให้ = ไม่นับว่าปลอดภัย สคริปต์นี้จึงข้ามหน้าจอทั้งหมด
    แล้วส่ง POST/GET ตรงไปที่ action พร้อม id ของสาขาอื่น เหมือนคนที่แก้ URL เอง

    ใช้:  pwsh -File tools\Test-BranchIsolation.ps1
    ต้องมีเว็บรันอยู่ที่ http://localhost:52080 (ดู README)
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:52080'
)

$ErrorActionPreference = 'Stop'

$script:Passed = 0
$script:Failed = 0

function Get-Token($response) {
    if ($response.Content -match 'name="__RequestVerificationToken"[^>]*value="([^"]+)"') {
        return $Matches[1]
    }
    throw 'ไม่พบ __RequestVerificationToken'
}

function New-Session([string] $empCode) {
    $session = $null
    $login = Invoke-WebRequest "$BaseUrl/Account/Login" -SessionVariable session -UseBasicParsing
    Invoke-WebRequest "$BaseUrl/Account/Login" -Method Post -WebSession $session -UseBasicParsing -Body @{
        '__RequestVerificationToken' = (Get-Token $login)
        'EmpCode'                    = $empCode
    } | Out-Null
    return $session
}

function Assert([string] $title, [bool] $condition, [string] $detail = '') {
    if ($condition) {
        Write-Host "   [ผ่าน] $title" -ForegroundColor Green
        $script:Passed++
    } else {
        Write-Host "   [ไม่ผ่าน] $title $detail" -ForegroundColor Red
        $script:Failed++
    }
}

function Invoke-Attack {
    param([string] $Url, [Microsoft.PowerShell.Commands.WebRequestSession] $Session,
          [hashtable] $Body, [string] $TokenFrom = '/Requests')

    if ($Body) {
        $page = Invoke-WebRequest "$BaseUrl$TokenFrom" -WebSession $Session -UseBasicParsing -SkipHttpErrorCheck
        $Body['__RequestVerificationToken'] = (Get-Token $page)
        return Invoke-WebRequest "$BaseUrl$Url" -Method Post -WebSession $Session -UseBasicParsing -Body $Body -SkipHttpErrorCheck
    }

    return Invoke-WebRequest "$BaseUrl$Url" -WebSession $Session -UseBasicParsing -SkipHttpErrorCheck
}

Write-Host ''
Write-Host '=== เตรียมข้อมูล : สร้างใบงานของสาขา SDC ===' -ForegroundColor Cyan

$sdcUser = New-Session '10002'        # U-User สาขา SDC (เจ้าของใบงาน)
$sdcOther = New-Session '10004'       # U-User สาขา SDC (คนละคนกับเจ้าของ)
$sdcMessenger = New-Session '10003'   # M-Messenger สาขา SDC
$sbkMessenger = New-Session '20003'   # M-Messenger สาขา SBK (คนละสาขา)
$sbkAdmin = New-Session '20001'       # A-Admin สาขา SBK (คนละสาขา)

$createPage = Invoke-WebRequest "$BaseUrl/Requests/Create" -WebSession $sdcUser -UseBasicParsing
$body = @{
    '__RequestVerificationToken' = (Get-Token $createPage)
    'RequesterEmpCode'           = '10002'
    'SendDate'                   = (Get-Date).ToString('yyyy-MM-dd')
    'ContactName'                = 'บริษัท ทดสอบการแยกสาขา จำกัด'
    'Address'                    = '1 ถนนทดสอบ'
    'Detail'                     = 'ใบงานสำหรับทดสอบ BR-6'
    'IsPersonal'                 = 'false'
    'JobTypes[0].Code'           = 'SendDoc'; 'JobTypes[0].Selected' = 'true'
    'JobTypes[1].Code'           = 'ReceiveDoc'; 'JobTypes[1].Selected' = 'false'
    'JobTypes[2].Code'           = 'ReceiveCheck'; 'JobTypes[2].Selected' = 'false'
    'JobTypes[3].Code'           = 'PlaceBill'; 'JobTypes[3].Selected' = 'false'
    'JobTypes[4].Code'           = 'RenewTax'; 'JobTypes[4].Selected' = 'false'
    'JobTypes[5].Code'           = 'Other'; 'JobTypes[5].Selected' = 'false'
}
$created = Invoke-WebRequest "$BaseUrl/Requests/Create" -Method Post -WebSession $sdcUser -UseBasicParsing -Body $body
if ($created.BaseResponse.RequestMessage.RequestUri.AbsolutePath -match '/Requests/Details/(\d+)') {
    $reqId = $Matches[1]
} else {
    throw 'สร้างใบงานสำหรับทดสอบไม่สำเร็จ'
}
if ($created.Content -match 'MSG-SDC-\d{4}-\d{4}') { $reqNo = $Matches[0] }
Write-Host "   ใบงานทดสอบ $reqNo (reqId=$reqId) สาขา SDC" -ForegroundColor DarkGray

# ยืนยันรับงานเพื่อให้มีรูป/assignment ไว้ทดสอบด้วย
Invoke-Attack -Url '/Requests/ChangeStatus' -Session $sdcMessenger -TokenFrom "/Requests/Details/$reqId" -Body @{
    id = $reqId; statusAction = 'Confirm'; returnTo = 'details' } | Out-Null

Write-Host ''
Write-Host '=== 1. อ่านข้อมูลข้ามสาขา (BR-6) ===' -ForegroundColor Cyan

$r = Invoke-Attack "/Requests/Details/$reqId" $sbkMessenger
Assert 'Messenger สาขาอื่นเปิดรายละเอียดใบงานไม่ได้' ($r.StatusCode -eq 404) "(ได้ $($r.StatusCode))"

$r = Invoke-Attack "/Requests/Details/$reqId" $sbkAdmin
Assert 'Admin สาขาอื่นเปิดรายละเอียดใบงานไม่ได้' ($r.StatusCode -eq 404) "(ได้ $($r.StatusCode))"

$r = Invoke-Attack "/Requests/Edit/$reqId" $sbkAdmin
Assert 'Admin สาขาอื่นเปิดฟอร์มแก้ไขไม่ได้' ($r.StatusCode -eq 404) "(ได้ $($r.StatusCode))"

$r = Invoke-Attack '/Requests' $sbkMessenger
Assert 'รายการใบงานของสาขาอื่นไม่มีใบงานสาขา SDC' ($r.Content -notmatch [regex]::Escape($reqNo))

$r = Invoke-Attack "/Queue?date=$((Get-Date).ToString('yyyy-MM-dd'))" $sbkMessenger
Assert 'คิวงานของสาขาอื่นไม่มีใบงานสาขา SDC' ($r.Content -notmatch [regex]::Escape($reqNo))

$r = Invoke-Attack "/Reports?dateFrom=$((Get-Date).ToString('yyyy-MM-dd'))&dateTo=$((Get-Date).ToString('yyyy-MM-dd'))" $sbkAdmin
Assert 'รายงานของสาขาอื่นไม่มีใบงานสาขา SDC' ($r.Content -notmatch [regex]::Escape($reqNo))

Write-Host ''
Write-Host '=== 2. เขียนข้อมูลข้ามสาขา (BR-6) ===' -ForegroundColor Cyan

foreach ($action in @('Confirm', 'Complete', 'Cancel', 'Pause', 'Resume')) {
    $r = Invoke-Attack -Url '/Requests/ChangeStatus' -Session $sbkMessenger -Body @{
        id = $reqId; statusAction = $action; reason = 'เจาะระบบ'; returnTo = 'details' }
    $blocked = $r.Content -match 'ไม่พบใบแจ้งงาน' -or $r.StatusCode -eq 404
    Assert "เปลี่ยนสถานะ ($action) ข้ามสาขาไม่ได้" $blocked
}

$r = Invoke-Attack -Url '/Requests/ConfirmReceipt' -Session $sbkMessenger -Body @{
    id = $reqId; returnTo = 'details' }
Assert 'ยืนยันรับของข้ามสาขาไม่ได้' ($r.Content -match 'ไม่พบใบแจ้งงาน' -or $r.StatusCode -eq 404)

$r = Invoke-Attack -Url '/Queue/Move' -Session $sbkMessenger -TokenFrom '/Queue' -Body @{
    id = $reqId; direction = 'Up'; date = (Get-Date).ToString('yyyy-MM-dd') }
Assert 'จัดลำดับคิวข้ามสาขาไม่ได้' ($r.Content -match 'ไม่พบใบแจ้งงาน')

$r = Invoke-Attack -Url '/Photos/Delete' -Session $sbkMessenger -Body @{ id = 1; reqId = $reqId }
Assert 'ลบรูปข้ามสาขาไม่ได้' ($r.Content -match 'ไม่พบรูปนี้' -or $r.Content -match 'ไม่พบใบแจ้งงาน')

# แก้ไขใบงานข้ามสาขา : POST ตรงพร้อม rowVersion มั่ว
$r = Invoke-Attack -Url '/Requests/Edit' -Session $sbkAdmin -TokenFrom '/Requests' -Body @{
    ReqId = $reqId; RowVersion = 'AAAAAAAAB9A='; RequesterEmpCode = '20002'
    SendDate = (Get-Date).ToString('yyyy-MM-dd'); ContactName = 'ถูกแก้โดยสาขาอื่น'
    Address = 'x'; Detail = 'x'; IsPersonal = 'false'
    'JobTypes[0].Code' = 'SendDoc'; 'JobTypes[0].Selected' = 'true' }
Assert 'แก้ไขใบงานข้ามสาขาไม่ได้' ($r.Content -match 'ไม่พบใบแจ้งงาน')

$check = Invoke-Attack "/Requests/Details/$reqId" $sdcMessenger
Assert 'ข้อมูลใบงานเดิมไม่ถูกแก้จากภายนอกสาขา' ($check.Content -match 'บริษัท ทดสอบการแยกสาขา จำกัด')

Write-Host ''
Write-Host '=== 3. สิทธิ์ตาม role ในสาขาเดียวกัน (§5 + D7) ===' -ForegroundColor Cyan

$r = Invoke-Attack '/Queue' $sdcOther
Assert 'U-User เข้าหน้าคิวงานไม่ได้' ($r.Content -match 'เปิดให้เฉพาะ Messenger')

$r = Invoke-Attack "/Requests/Details/$reqId" $sdcOther
Assert 'U-User เปิดใบงานของคนอื่นไม่ได้' ($r.StatusCode -eq 404) "(ได้ $($r.StatusCode))"

# ใบงานใบที่สองที่ยังอยู่สถานะ "รับแจ้ง" — ใช้ทดสอบสิทธิ์ตรง ๆ โดยไม่ติดเรื่องสถานะ
$createPage2 = Invoke-WebRequest "$BaseUrl/Requests/Create" -WebSession $sdcUser -UseBasicParsing
$body['__RequestVerificationToken'] = (Get-Token $createPage2)
$body['ContactName'] = 'ใบที่ยังไม่ถูกรับงาน'
$created2 = Invoke-WebRequest "$BaseUrl/Requests/Create" -Method Post -WebSession $sdcUser -UseBasicParsing -Body $body
if ($created2.BaseResponse.RequestMessage.RequestUri.AbsolutePath -match '/Requests/Details/(\d+)') {
    $receivedReqId = $Matches[1]
} else {
    throw 'สร้างใบงานใบที่สองไม่สำเร็จ'
}

$r = Invoke-Attack -Url '/Requests/ChangeStatus' -Session $sdcUser -TokenFrom "/Requests/Details/$receivedReqId" -Body @{
    id = $receivedReqId; statusAction = 'Confirm'; returnTo = 'details' }
Assert 'เจ้าของใบงาน (U-User) ยืนยันรับงานเองไม่ได้' ($r.Content -match 'สิทธิ์ของเจ้าหน้าที่')

Invoke-Attack -Url '/Requests/ChangeStatus' -Session $sdcOther -TokenFrom '/Requests' -Body @{
    id = $receivedReqId; statusAction = 'Cancel'; returnTo = 'details' } | Out-Null

# ดูผลจริงที่ใบงาน : ต้องยังเป็น "รับแจ้ง" อยู่ ไม่ถูกยกเลิกโดยคนอื่น
$afterAttack = Invoke-Attack "/Requests/Details/$receivedReqId" $sdcUser
Assert 'U-User ยกเลิกใบของคนอื่นไม่ได้' ($afterAttack.Content -match 'รับแจ้ง' -and $afterAttack.Content -notmatch 'badge text-bg-secondary">ยกเลิก')

$r = Invoke-Attack -Url '/Photos/Upload' -Session $sdcUser -TokenFrom "/Requests/Details/$receivedReqId" -Body @{
    reqId = $receivedReqId; photoType = 'send' }
Assert 'U-User อัปโหลดรูปไม่ได้' ($r.Content -match 'สิทธิ์ของเจ้าหน้าที่' -or $r.Content -match 'ไม่พบไฟล์รูป')

$r = Invoke-Attack -Url '/Requests/ChangeStatus' -Session $sdcUser -TokenFrom "/Requests/Details/$reqId" -Body @{
    id = $reqId; statusAction = 'Cancel'; returnTo = 'details' }
Assert 'เจ้าของใบงานยกเลิกไม่ได้แล้วหลัง Messenger รับงาน (D7)' ($r.Content -match 'สิทธิ์ของเจ้าหน้าที่')

$r = Invoke-Attack -Url '/Requests/ChangeStatus' -Session $sdcMessenger -TokenFrom "/Requests/Details/$reqId" -Body @{
    id = $reqId; statusAction = 'Complete'; returnTo = 'details' }
Assert 'ปิดงานในสาขาตัวเองได้ตามปกติ (ควบคุมกลุ่ม)' ($r.Content -match 'เสร็จงานแล้ว')

Write-Host ''
Write-Host '=== 4. CSRF + การเข้าถึงโดยไม่ login ===' -ForegroundColor Cyan

$noToken = Invoke-WebRequest "$BaseUrl/Requests/ChangeStatus" -Method Post -WebSession $sdcMessenger `
    -UseBasicParsing -SkipHttpErrorCheck -Body @{ id = $reqId; statusAction = 'Cancel'; returnTo = 'details' }
Assert 'POST ที่ไม่มี antiforgery token ถูกปฏิเสธ' ($noToken.StatusCode -ge 400) "(ได้ $($noToken.StatusCode))"

# ปล่อยให้ตาม redirect ไปจนสุด แล้วดูว่าไปจบที่หน้า login จริงไหม
$anonymous = Invoke-WebRequest "$BaseUrl/Requests" -UseBasicParsing -SkipHttpErrorCheck
$landedOnLogin = $anonymous.BaseResponse.RequestMessage.RequestUri.AbsolutePath -match 'Account/Login'
Assert 'คนที่ยังไม่ login ถูกส่งไปหน้า login' $landedOnLogin `
    "(ไปจบที่ $($anonymous.BaseResponse.RequestMessage.RequestUri.AbsolutePath))"

$anonymousPhoto = Invoke-WebRequest "$BaseUrl/Photos/Show/1" -UseBasicParsing -SkipHttpErrorCheck
$photoBlocked = $anonymousPhoto.BaseResponse.RequestMessage.RequestUri.AbsolutePath -match 'Account/Login'
Assert 'คนที่ยังไม่ login เปิดรูปไม่ได้' $photoBlocked

$anonymousReport = Invoke-WebRequest "$BaseUrl/Reports/Export" -UseBasicParsing -SkipHttpErrorCheck
$reportBlocked = $anonymousReport.BaseResponse.RequestMessage.RequestUri.AbsolutePath -match 'Account/Login'
Assert 'คนที่ยังไม่ login ดาวน์โหลดรายงานไม่ได้' $reportBlocked

Write-Host ''
Write-Host '=== 5. security header ===' -ForegroundColor Cyan

$headers = (Invoke-WebRequest "$BaseUrl/Requests" -WebSession $sdcMessenger -UseBasicParsing).Headers

# ค่าใน .Headers ของ PowerShell 7 เป็น array เสมอ จึงต้องรวมเป็นสตริงก่อนเทียบ
function Get-Header([string] $name) { return (@($headers[$name]) -join '') }

Assert 'มี X-Content-Type-Options: nosniff' ((Get-Header 'X-Content-Type-Options') -eq 'nosniff')
Assert 'มี X-Frame-Options' ((Get-Header 'X-Frame-Options').Length -gt 0)
Assert 'มี Referrer-Policy' ((Get-Header 'Referrer-Policy').Length -gt 0)
Assert 'ไม่ส่ง X-Powered-By ออกไป' ((Get-Header 'X-Powered-By').Length -eq 0)
Assert 'ไม่ส่ง X-AspNet-Version ออกไป' ((Get-Header 'X-AspNet-Version').Length -eq 0)

Write-Host ''
if ($script:Failed -eq 0) {
    Write-Host "สรุป : ผ่านทั้งหมด $($script:Passed) ข้อ" -ForegroundColor Green
    exit 0
}

Write-Host "สรุป : ผ่าน $($script:Passed) ข้อ · ไม่ผ่าน $($script:Failed) ข้อ" -ForegroundColor Red
exit 1
