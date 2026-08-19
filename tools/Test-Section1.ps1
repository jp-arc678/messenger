<#
    ทดสอบ UAT หมวด 1 — เข้าสู่ระบบ + สิทธิ์ (Phase 0 · BR-7, D10)
    ครอบคลุม 1.1 - 1.6

    ใช้:  pwsh -File tools\Test-Section1.ps1
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

<#
    login แบบ "ไม่โยน exception เมื่อเข้าไม่ได้" — หมวด 1 ต้องทดสอบรหัสที่ SSO ไม่รู้จักด้วย
    จึงใช้ตัวนี้แทน New-Session ของ TestSupport ที่ถือว่าการ login ต้องสำเร็จเสมอ
#>
function Try-Login([string] $empCode) {
    $s = $null
    $login = Invoke-WebRequest "$BaseUrl/Account/Login" -SessionVariable s -UseBasicParsing
    $resp = Invoke-WebRequest "$BaseUrl/Account/Login" -Method Post -WebSession $s -UseBasicParsing -SkipHttpErrorCheck -Body @{
        '__RequestVerificationToken' = (Get-Token $login); 'EmpCode' = $empCode }
    return [pscustomobject]@{ Session = $s; Response = $resp }
}

# ---------------------------------------------------------------- 1.1
Write-Host ''
Write-Host '=== 1.1 เปิดหน้าแรกโดยยังไม่ login ===' -ForegroundColor Cyan
$r = Invoke-WebRequest "$BaseUrl/" -UseBasicParsing -SkipHttpErrorCheck
$final = $r.BaseResponse.RequestMessage.RequestUri.ToString()
Assert "ถูกส่งไปหน้าเข้าสู่ระบบ : $final" ($final -match '/Account/Login')
Assert 'มี ReturnUrl ติดไปด้วย (กลับไปหน้าเดิมได้หลัง login)' ($final -match 'ReturnUrl')
Assert 'หน้าที่ได้เป็นฟอร์ม login จริง' ($r.Content -match 'name="EmpCode"')

# ---------------------------------------------------------------- 1.2
Write-Host ''
Write-Host '=== 1.2 login ด้วย 10002 ===' -ForegroundColor Cyan
$a = Try-Login '10002'
$page10002 = Invoke-WebRequest "$BaseUrl/" -WebSession $a.Session -UseBasicParsing -SkipHttpErrorCheck
Assert 'เข้าระบบได้ (หน้าแรกตอบ 200)' ($page10002.StatusCode -eq 200) "(ได้ $($page10002.StatusCode))"
Assert 'แถบบนขึ้นชื่อ "สมหญิง รักงาน"' ($page10002.Content -match 'สมหญิง รักงาน')
Assert 'แถบบนขึ้น "สาขา SDC"' ($page10002.Content -match 'สาขา\s*SDC')
Assert 'แถบบนขึ้นสิทธิ์ "พนักงานทั่วไป (User)"' ($page10002.Content -match 'พนักงานทั่วไป \(User\)')

# ---------------------------------------------------------------- 1.5
Write-Host ''
Write-Host '=== 1.5 เมนูของ User (10002) ===' -ForegroundColor Cyan
Assert 'ไม่มีเมนู "คิวงานประจำวัน"' ($page10002.Content -notmatch 'คิวงานประจำวัน')
# เข้า /Queue ตรง ๆ : ระบบตอบ 200 แต่ redirect ออกไป /Requests พร้อมข้อความปฏิเสธ
# (ไม่ใช่ 403 — ตรวจที่ "ไม่ได้เนื้อหาคิวงาน" ไม่ใช่ที่ status code)
$q = Invoke-WebRequest "$BaseUrl/Queue" -WebSession $a.Session -UseBasicParsing -SkipHttpErrorCheck
$qFinal = $q.BaseResponse.RequestMessage.RequestUri.AbsolutePath
Assert 'เข้า /Queue ตรง ๆ แล้วถูกเด้งออกไปหน้าอื่น (ไม่ใช่แค่ซ่อนเมนู)' ($qFinal -notmatch '(?i)^/Queue') "(ปลายทาง $qFinal)"
Assert 'ขึ้นข้อความปฏิเสธว่าเปิดให้เฉพาะ Messenger' ($q.Content -match 'เปิดให้เฉพาะ Messenger')
Assert 'ไม่มีหัวข้อ/เนื้อหาคิวงานหลุดมา' ($q.Content -notmatch 'รอยืนยัน' -and $q.Content -notmatch 'คิววิ่งงาน')

# ---------------------------------------------------------------- 1.6
Write-Host ''
Write-Host '=== 1.6 เมนูของ Messenger (10003) ===' -ForegroundColor Cyan
$b = Try-Login '10003'
$page10003 = Invoke-WebRequest "$BaseUrl/" -WebSession $b.Session -UseBasicParsing -SkipHttpErrorCheck
Assert 'มีเมนู "คิวงานประจำวัน"' ($page10003.Content -match 'คิวงานประจำวัน')
Assert 'สิทธิ์ขึ้นเป็น "เจ้าหน้าที่รับ-ส่งเอกสาร (Messenger)"' ($page10003.Content -match 'เจ้าหน้าที่รับ-ส่งเอกสาร \(Messenger\)')
$q3 = Invoke-WebRequest "$BaseUrl/Queue" -WebSession $b.Session -UseBasicParsing -SkipHttpErrorCheck
Assert 'เข้าหน้าคิวงานได้จริง' ($q3.StatusCode -eq 200) "(ได้ $($q3.StatusCode))"

# ---------------------------------------------------------------- 1.4
Write-Host ''
Write-Host '=== 1.4 login ด้วยรหัสมั่ว 99999 ===' -ForegroundColor Cyan
$c = Try-Login '99999'
$pageBad = Invoke-WebRequest "$BaseUrl/" -WebSession $c.Session -UseBasicParsing -SkipHttpErrorCheck
$finalBad = $pageBad.BaseResponse.RequestMessage.RequestUri.ToString()
Assert "ขึ้นข้อความว่า SSO ไม่พบรหัสนี้" ($c.Response.Content -match "ระบบ SSO ไม่พบรหัสพนักงาน")
Assert 'ไม่ได้เข้าระบบ (เปิดหน้าแรกแล้วยังถูกเด้งไป login)' ($finalBad -match '/Account/Login') "(ปลายทาง $finalBad)"

# ---------------------------------------------------------------- 1.3
Write-Host ''
Write-Host '=== 1.3 login ด้วย 20099 (ยังไม่มีในตาราง Employee) ===' -ForegroundColor Cyan
$d = Try-Login '20099'
$page20099 = Invoke-WebRequest "$BaseUrl/" -WebSession $d.Session -UseBasicParsing -SkipHttpErrorCheck
Assert 'เข้าระบบได้' ($page20099.StatusCode -eq 200) "(ได้ $($page20099.StatusCode))"
Assert 'ได้สิทธิ์ "พนักงานทั่วไป (User)" อัตโนมัติ (D10)' ($page20099.Content -match 'พนักงานทั่วไป \(User\)')
Assert 'สาขามาจาก SSO ถูกต้อง (SBK)' ($page20099.Content -match 'สาขา\s*SBK')
Assert 'ชื่อมาจาก SSO' ($page20099.Content -match 'พนักงานใหม่ สาขา SBK')
Assert 'ไม่มีเมนูคิวงาน (เพราะเป็น User)' ($page20099.Content -notmatch 'คิวงานประจำวัน')

exit (Complete-TestRun $sinceReqId -KeepData:$KeepData)
