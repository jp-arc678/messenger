<#
    Test-UploadTooLarge.ps1 — พิสูจน์ UAT-01
    ไฟล์ที่ใหญ่เกิน maxRequestLength (8 MB) ต้องได้ HTTP 413 พร้อมข้อความภาษาไทย
    ไม่ใช่ HTTP 500 เปล่า ๆ เหมือนเดิม · และเส้นทางปกติต้องไม่พัง
#>
[CmdletBinding()]
param([string] $BaseUrl = 'http://localhost:52080')

$ErrorActionPreference = 'Stop'
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

function New-TestFile([string] $path, [string] $kind, [int] $sizeBytes) {
    $header = switch ($kind) {
        'png' { [byte[]] @(0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A) }
        'pdf' { [Text.Encoding]::ASCII.GetBytes('%PDF-1.4') }
    }
    $bytes = New-Object byte[] $sizeBytes
    [Array]::Copy($header, $bytes, $header.Length)
    [IO.File]::WriteAllBytes($path, $bytes)
    return Get-Item $path
}

function Invoke-Upload($session, [int] $reqId, [IO.FileInfo] $file) {
    $page = Invoke-WebRequest "$BaseUrl/Requests/Details/$reqId" -WebSession $session -UseBasicParsing
    try {
        return Invoke-WebRequest "$BaseUrl/Photos/Upload" -Method Post -WebSession $session -UseBasicParsing `
            -SkipHttpErrorCheck -Form @{
                '__RequestVerificationToken' = (Get-Token $page)
                'reqId'                      = "$reqId"
                'photoType'                  = 'send'
                'file'                        = $file
            }
    } catch [Microsoft.PowerShell.Commands.HttpResponseException] {
        return $_.Exception.Response
    }
}

Write-Host ''
Write-Host '=== เตรียมใบงานสถานะ กำลังส่ง (D23 ต้องเป็น Delivering/Paused ถึงอัปโหลดรูปได้) ===' -ForegroundColor Cyan

$user = New-Session '10002'        # U-User สาขา SDC
$messenger = New-Session '10003'   # M-Messenger สาขา SDC

$createPage = Invoke-WebRequest "$BaseUrl/Requests/Create" -WebSession $user -UseBasicParsing
$created = Invoke-WebRequest "$BaseUrl/Requests/Create" -Method Post -WebSession $user -UseBasicParsing -Body @{
    '__RequestVerificationToken' = (Get-Token $createPage)
    'RequesterEmpCode'           = '10002'
    'SendDate'                   = (Get-Date).ToString('yyyy-MM-dd')
    'ContactName'                = 'บริษัท ทดสอบไฟล์ใหญ่ จำกัด'
    'Address'                    = '1 ถนนทดสอบ UAT-01'
    'Detail'                     = 'ใบงานสำหรับทดสอบ UAT-01 (413)'
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
if ($created.Content -match 'MSG-SDC-\d{4}-\d{4}') { $reqNo = $Matches[0] }

$detailsPage = Invoke-WebRequest "$BaseUrl/Requests/Details/$reqId" -WebSession $messenger -UseBasicParsing
Invoke-WebRequest "$BaseUrl/Requests/ChangeStatus" -Method Post -WebSession $messenger -UseBasicParsing -Body @{
    '__RequestVerificationToken' = (Get-Token $detailsPage)
    'id' = "$reqId"; 'statusAction' = 'Confirm'; 'returnTo' = 'details' } | Out-Null
Write-Host "   ใบงาน $reqNo (reqId=$reqId) สถานะ กำลังส่ง" -ForegroundColor DarkGray

# เขียนไฟล์ทดสอบขนาดใหญ่ลง temp ของเครื่อง ไม่ใช่ในโฟลเดอร์โปรเจกต์
$dir = [IO.Path]::GetTempPath()
$pdfBig   = New-TestFile (Join-Path $dir 'big.pdf')   'pdf' (12MB)
$pngBig   = New-TestFile (Join-Path $dir 'big.png')   'png' ([int](10.57 * 1MB))
$pngOver2 = New-TestFile (Join-Path $dir 'over2.png') 'png' ([int](2.64 * 1MB))
$pdfSmall = New-TestFile (Join-Path $dir 'small.pdf') 'pdf' (1MB)

Write-Host ''
Write-Host '=== UAT-01 : ไฟล์เกิน maxRequestLength (8 MB) ต้องได้ 413 ไม่ใช่ 500 ===' -ForegroundColor Cyan

$r = Invoke-Upload $messenger $reqId $pdfBig
Assert 'PDF 12 MB ได้ HTTP 413' ($r.StatusCode -eq 413) "(ได้ $($r.StatusCode))"
Assert 'PDF 12 MB มีข้อความอธิบายภาษาไทย' ("$($r.Content)" -match 'ใหญ่เกินกว่าที่ระบบรับได้')

$r = Invoke-Upload $messenger $reqId $pngBig
Assert 'PNG 10.57 MB ได้ HTTP 413' ($r.StatusCode -eq 413) "(ได้ $($r.StatusCode))"
Assert 'PNG 10.57 MB มีข้อความอธิบายภาษาไทย' ("$($r.Content)" -match 'ใหญ่เกินกว่าที่ระบบรับได้')

Write-Host ''
Write-Host '=== ไม่ถดถอย : ไฟล์ที่เล็กกว่า 8 MB ยังได้ข้อความเดิม ===' -ForegroundColor Cyan

$r = Invoke-Upload $messenger $reqId $pngOver2
Assert 'PNG 2.64 MB ไม่ได้ 413 (เข้าถึง controller ตามปกติ)' ($r.StatusCode -eq 200) "(ได้ $($r.StatusCode))"
Assert 'PNG 2.64 MB ขึ้นข้อความ "ไฟล์ใหญ่เกิน 2 MB"' ("$($r.Content)" -match 'ไฟล์ใหญ่เกิน 2 MB')

$r = Invoke-Upload $messenger $reqId $pdfSmall
Assert 'PDF 1 MB ไม่ได้ 413 (เข้าถึง controller ตามปกติ)' ($r.StatusCode -eq 200) "(ได้ $($r.StatusCode))"
Assert 'PDF 1 MB ขึ้นข้อความ "รองรับเฉพาะไฟล์รูปแบบ JPG และ PNG"' ("$($r.Content)" -match 'รองรับเฉพาะไฟล์รูปแบบ JPG และ PNG')

Remove-Item $pdfBig, $pngBig, $pngOver2, $pdfSmall -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host "สรุป : ผ่าน $script:Passed · ไม่ผ่าน $script:Failed" -ForegroundColor $(if ($script:Failed -eq 0) { 'Green' } else { 'Red' })
exit ([int] ($script:Failed -gt 0))
