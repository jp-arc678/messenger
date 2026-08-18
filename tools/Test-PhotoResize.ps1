<#
    Test-PhotoResize.ps1 — UAT 4.5 / BR-3 : ย่อรูปฝั่ง client ให้ ≤ 2 MB ก่อนอัปโหลด

    ข้อนี้ทดสอบด้วยการยิง HTTP ตรงไม่ได้ เพราะการย่อรูปเกิดใน <canvas> ของเบราว์เซอร์
    (`Scripts\photo-upload.js`) สคริปต์นี้จึงสั่ง Chrome จริงแบบ headless ให้กดแทนคน
    แล้ววัด "ไฟล์ที่ถูกส่งขึ้น server จริง ๆ" จาก Content-Length ที่เบราว์เซอร์ส่งออกไป

    ทดสอบ 3 เคส :
      1. รูปใหญ่ 4032×3024 (~6.5 MB เท่ารูปจากกล้องมือถือ) → ต้องถูกย่อเหลือ ≤ 2 MB แล้วอัปสำเร็จ
      2. รูปเล็กที่ไม่ถึง 2 MB อยู่แล้ว                      → ต้องส่งไฟล์เดิม ไม่แปลงซ้ำให้เสียคุณภาพ
      3. ไฟล์ PDF                                            → ต้องถูกปฏิเสธตั้งแต่ก่อนส่ง (ไม่มี request ออกไปเลย)

    ใช้:  pwsh -File tools\Test-PhotoResize.ps1
    ต้องมี : เว็บรันอยู่ที่ http://localhost:52080 · Chrome หรือ Edge · Node.js 22 ขึ้นไป
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:52080',

    # รหัสพนักงานที่ใช้อัปโหลด — ต้องเป็น Messenger/Admin ของสาขาเดียวกับใบงาน (D23)
    [string] $EmpCode = '10003',

    # ผู้แจ้งงาน (สาขาเดียวกัน)
    [string] $RequesterEmpCode = '10002'
)

$ErrorActionPreference = 'Stop'
$script:Passed = 0
$script:Failed = 0

$probe = Join-Path $PSScriptRoot 'photo-resize-probe.mjs'
$maxBytes = 2MB

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

<#
    รูปทดสอบต้องเป็นไฟล์ JPEG ที่ "ถอดรหัสได้จริง" เพราะ photo-upload.js ต้องวาดลง canvas
    ไฟล์ขยะที่แค่มี magic bytes ถูกจะถูกปฏิเสธตั้งแต่ loadImage() แล้วเข้าใจผิดว่าเป็นข้อบกพร่อง

    ลายที่วาดจงใจให้เหมือนภาพถ่าย (ไล่เฉดนุ่ม + noise เม็ดเล็ก) เพราะภาพ noise ล้วนบีบอัดไม่ลง
    ส่วนภาพเรียบล้วนก็เล็กเกินกว่าจะทดสอบการย่อ
#>
function New-PhotoLikeJpeg([string] $path, [int] $width, [int] $height, [int] $quality) {
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $rnd = New-Object Random 7
    $rect = New-Object System.Drawing.Rectangle 0, 0, $width, $height
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    try {
        $bytes = New-Object byte[] ($data.Stride * $height)
        for ($y = 0; $y -lt $height; $y++) {
            $row = $y * $data.Stride
            for ($x = 0; $x -lt $width; $x++) {
                $i = $row + $x * 3
                $g = [int](128 + 100 * [Math]::Sin($x / 180.0) * [Math]::Cos($y / 220.0)) + $rnd.Next(-28, 28)
                if ($g -lt 0) { $g = 0 } elseif ($g -gt 255) { $g = 255 }
                $bytes[$i]     = [byte] $g
                $bytes[$i + 1] = [byte] [Math]::Min(255, [Math]::Max(0, $g * 0.8 + $rnd.Next(-20, 20)))
                $bytes[$i + 2] = [byte] [Math]::Min(255, [Math]::Max(0, 255 - $g * 0.6 + $rnd.Next(-20, 20)))
            }
        }
        [System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $bytes.Length)
    }
    finally { $bmp.UnlockBits($data) }

    $encoder = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
    $params = New-Object System.Drawing.Imaging.EncoderParameters 1
    $params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter (
        [System.Drawing.Imaging.Encoder]::Quality), ([long] $quality)
    $bmp.Save($path, $encoder, $params)
    $bmp.Dispose()
    return Get-Item $path
}

function Invoke-Probe([int] $reqId, [string] $file, [string] $photoType = 'send') {
    $output = & node $probe $BaseUrl $EmpCode "$reqId" $file $photoType 2>&1
    $line = $output | Where-Object { "$_" -like '===RESULT===*' } | Select-Object -First 1
    if (-not $line) { throw "probe ไม่คืนผล : $($output -join [Environment]::NewLine)" }
    return ("$line" -replace '^===RESULT===', '') | ConvertFrom-Json
}

# ---------------------------------------------------------------- ตรวจเครื่องมือ

if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'ไม่พบ Node.js — สคริปต์นี้ต้องใช้ Node 22 ขึ้นไป' }
$nodeMajor = [int] (((& node --version) -replace '^v', '') -split '\.')[0]
if ($nodeMajor -lt 22) { throw "Node $nodeMajor เก่าเกินไป — ต้อง 22 ขึ้นไป (ใช้ WebSocket ที่มีมาในตัว)" }

# ---------------------------------------------------------------- เตรียมใบงาน

Write-Host ''
Write-Host '=== เตรียมใบงานสถานะ กำลังส่ง (D23 อัปโหลดรูปได้เฉพาะ Delivering/Paused) ===' -ForegroundColor Cyan

$user = New-Session $RequesterEmpCode
$uploader = New-Session $EmpCode

$createPage = Invoke-WebRequest "$BaseUrl/Requests/Create" -WebSession $user -UseBasicParsing
$created = Invoke-WebRequest "$BaseUrl/Requests/Create" -Method Post -WebSession $user -UseBasicParsing -Body @{
    '__RequestVerificationToken' = (Get-Token $createPage)
    'RequesterEmpCode'           = $RequesterEmpCode
    'SendDate'                   = (Get-Date).ToString('yyyy-MM-dd')
    'ContactName'                = 'บริษัท ทดสอบย่อรูป จำกัด'
    'Address'                    = '1 ถนนทดสอบ UAT 4.5'
    'Detail'                     = 'ใบงานสำหรับทดสอบการย่อรูปฝั่ง client'
    'IsPersonal'                 = 'false'
    'JobTypes[0].Code' = 'SendDoc';      'JobTypes[0].Selected' = 'true'
    'JobTypes[1].Code' = 'ReceiveDoc';   'JobTypes[1].Selected' = 'false'
    'JobTypes[2].Code' = 'ReceiveCheck'; 'JobTypes[2].Selected' = 'false'
    'JobTypes[3].Code' = 'PlaceBill';    'JobTypes[3].Selected' = 'false'
    'JobTypes[4].Code' = 'RenewTax';     'JobTypes[4].Selected' = 'false'
    'JobTypes[5].Code' = 'Other';        'JobTypes[5].Selected' = 'false'
}
if ($created.BaseResponse.RequestMessage.RequestUri.AbsolutePath -match '/Requests/Details/(\d+)') { $reqId = [int] $Matches[1] }
else { throw 'สร้างใบงานทดสอบไม่สำเร็จ' }
if ($created.Content -match 'MSG-[A-Z]{3}-\d{4}-\d{4}') { $reqNo = $Matches[0] }

$detailsPage = Invoke-WebRequest "$BaseUrl/Requests/Details/$reqId" -WebSession $uploader -UseBasicParsing
Invoke-WebRequest "$BaseUrl/Requests/ChangeStatus" -Method Post -WebSession $uploader -UseBasicParsing -Body @{
    '__RequestVerificationToken' = (Get-Token $detailsPage)
    'id' = "$reqId"; 'statusAction' = 'Confirm'; 'returnTo' = 'details' } | Out-Null
Write-Host "   ใบงาน $reqNo (reqId=$reqId) สถานะ กำลังส่ง" -ForegroundColor DarkGray

# ---------------------------------------------------------------- เตรียมไฟล์

Write-Host ''
Write-Host '=== สร้างไฟล์ทดสอบ ===' -ForegroundColor Cyan

$dir = Join-Path ([IO.Path]::GetTempPath()) 'UAT-4.5'
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$big = New-PhotoLikeJpeg (Join-Path $dir 'photo-big.jpg') 4032 3024 95
$small = New-PhotoLikeJpeg (Join-Path $dir 'photo-small.jpg') 1000 750 80
$pdf = Join-Path $dir 'document.pdf'
[IO.File]::WriteAllBytes($pdf, ([Text.Encoding]::ASCII.GetBytes('%PDF-1.4') + (New-Object byte[] 200000)))

Write-Host ("   photo-big.jpg   {0:N2} MB (4032x3024 เท่ารูปจากกล้องมือถือ)" -f ($big.Length / 1MB)) -ForegroundColor DarkGray
Write-Host ("   photo-small.jpg {0:N0} KB" -f ($small.Length / 1KB)) -ForegroundColor DarkGray

Assert 'ไฟล์ต้นทางใหญ่เกิน 2 MB จริง (ไม่งั้นเทสต์ไม่ได้พิสูจน์อะไร)' ($big.Length -gt $maxBytes) `
    ("(ได้ {0:N2} MB)" -f ($big.Length / 1MB))
Assert 'ไฟล์เล็กต่ำกว่า 2 MB จริง' ($small.Length -lt $maxBytes)

# ---------------------------------------------------------------- 1. รูปใหญ่

Write-Host ''
Write-Host '=== 1. รูปใหญ่จากกล้อง — ต้องถูกย่อก่อนส่ง (BR-3) ===' -ForegroundColor Cyan

$r = Invoke-Probe $reqId $big.FullName 'send'
Assert 'probe ทำงานจบโดยไม่มีข้อผิดพลาด' ($r.ok -eq $true) "($($r.error))"
Assert 'เบราว์เซอร์ส่ง request ออกไปจริง' ($r.outcome -eq 'sent') "(ได้ $($r.outcome) · $($r.hint))"

if ($r.outcome -eq 'sent') {
    Write-Host ("   ไฟล์ต้นทาง {0:N2} MB → ส่งจริง {1:N0} KB (Content-Length {2:N0} bytes)" -f `
        ($big.Length / 1MB), ($r.sentFile.size / 1KB), $r.contentLength) -ForegroundColor DarkGray

    Assert 'ไฟล์ที่ใส่ลงฟอร์มถูกย่อจนไม่เกิน 2 MB' ($r.sentFile.size -le $maxBytes) `
        ("(ได้ {0:N0} KB)" -f ($r.sentFile.size / 1KB))
    Assert 'Content-Length ที่เบราว์เซอร์ส่งจริงไม่เกิน 2 MB' ($r.contentLength -gt 0 -and $r.contentLength -le $maxBytes) `
        ("(ได้ {0:N0} bytes)" -f $r.contentLength)
    Assert 'ไฟล์ที่ส่งเล็กกว่าไฟล์ต้นทางอย่างมีนัยสำคัญ' ($r.sentFile.size -lt $big.Length / 2)
    Assert 'ถูกแปลงเป็น JPEG ชื่อ photo.jpg ตามที่สคริปต์ตั้งให้' `
        ($r.sentFile.name -eq 'photo.jpg' -and $r.sentFile.type -eq 'image/jpeg') `
        "(ได้ $($r.sentFile.name) · $($r.sentFile.type))"
    Assert 'server รับไว้และ redirect (302) ไม่ใช่ 413' ($r.sentFile.status -eq 302 -or $r.sentFile.status -eq 200) `
        "(ได้ $($r.sentFile.status))"
}

# รูปที่เก็บลง filesystem จริงต้องไม่เกิน 2 MB ด้วย (ยืนยันอีกชั้นว่าไม่ได้ผ่านมาแบบดิบ ๆ)
$after = Invoke-WebRequest "$BaseUrl/Requests/Details/$reqId" -WebSession $uploader -UseBasicParsing
$photoIds = [regex]::Matches($after.Content, '/Photos/Show/(\d+)') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
Assert 'รูปโผล่ในหน้ารายละเอียดใบงาน' ($photoIds.Count -ge 1)
foreach ($id in $photoIds) {
    $file = Invoke-WebRequest "$BaseUrl/Photos/Show/$id" -WebSession $uploader -UseBasicParsing
    Assert "ไฟล์ที่เก็บบน server (Photos/Show/$id) ไม่เกิน 2 MB" ($file.RawContentLength -le $maxBytes) `
        ("(ได้ {0:N0} KB)" -f ($file.RawContentLength / 1KB))
}

# ---------------------------------------------------------------- 2. รูปเล็ก

Write-Host ''
Write-Host '=== 2. รูปเล็กกว่า 2 MB อยู่แล้ว — ต้องส่งไฟล์เดิม ไม่แปลงซ้ำ ===' -ForegroundColor Cyan

$r = Invoke-Probe $reqId $small.FullName 'receive'
Assert 'อัปโหลดสำเร็จ' ($r.outcome -eq 'sent' -and ($r.sentFile.status -eq 302 -or $r.sentFile.status -eq 200)) `
    "(ได้ $($r.outcome) · status $($r.sentFile.status))"
Assert 'ส่งไฟล์เดิมไปทั้งก้อน (ชื่อไฟล์และขนาดไม่เปลี่ยน)' `
    ($r.sentFile.name -eq $small.Name -and $r.sentFile.size -eq $small.Length) `
    "(ได้ $($r.sentFile.name) · $($r.sentFile.size) bytes)"

# ---------------------------------------------------------------- 3. ไฟล์ที่ไม่ใช่รูป

Write-Host ''
Write-Host '=== 3. ไฟล์ PDF — ต้องถูกปฏิเสธตั้งแต่ก่อนส่ง (UAT-01) ===' -ForegroundColor Cyan

$r = Invoke-Probe $reqId $pdf 'send'
Assert 'ถูกปฏิเสธที่ฝั่งเบราว์เซอร์ ไม่ได้ส่งขึ้น server' ($r.outcome -eq 'rejected' -and -not $r.requestSent) `
    "(ได้ $($r.outcome) · requestSent=$($r.requestSent))"
Assert 'ขึ้นข้อความบอกให้เปลี่ยนเป็นไฟล์ JPG/PNG' ("$($r.hint)" -match 'JPG') "(ได้ $($r.hint))"

# ---------------------------------------------------------------- จบ

Remove-Item $big.FullName, $small.FullName, $pdf -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host "สรุป : ผ่าน $script:Passed · ไม่ผ่าน $script:Failed" -ForegroundColor $(if ($script:Failed -eq 0) { 'Green' } else { 'Red' })
exit ([int] ($script:Failed -gt 0))
