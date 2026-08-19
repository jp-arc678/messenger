<#
    ทดสอบ UAT หมวด 6 — รายงาน + export (Phase 5 · D29-D31)

    ใช้:  pwsh -File tools\Test-Section6.ps1
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
# ดึงคู่ "ป้าย = ตัวเลข" จากการ์ดสรุปด้านบนของหน้ารายงาน
function Get-Cards($html) {
    $cards = @{}
    foreach ($mt in [regex]::Matches($html, '<div class="text-body-secondary small">([^<]+)</div>\s*<div class="fs-3 fw-semibold">(\d+)</div>')) {
        $cards[$mt.Groups[1].Value.Trim()] = [int]$mt.Groups[2].Value
    }
    return $cards
}

$m = New-Session '10003'   # Messenger — เห็นทั้งสาขา
$u = New-Session '10002'   # User — เห็นเฉพาะใบตัวเอง

$monthStart = (Get-Date -Day 1).ToString('yyyy-MM-dd')
$today = (Get-Date).ToString('yyyy-MM-dd')

# ---------------------------------------------------------------- 6.1
Write-Host ''
Write-Host '=== 6.1 Messenger เปิดหน้ารายงาน ===' -ForegroundColor Cyan
$rep = Invoke-WebRequest "$BaseUrl/Reports" -WebSession $m -UseBasicParsing -SkipHttpErrorCheck
Assert 'เปิดได้' ($rep.StatusCode -eq 200) "(ได้ $($rep.StatusCode))"
Assert 'ขึ้นว่าเห็น "ทั้งสาขา"' ($rep.Content -match 'ทั้งสาขา')
$cards = Get-Cards $rep.Content
foreach ($label in 'ทั้งหมด','รับแจ้ง','กำลังส่ง','พักการส่ง','เสร็จงานแล้ว','ยกเลิก') {
    Assert "มีการ์ด '$label' (= $($cards[$label]))" ($cards.ContainsKey($label))
}
$sum = 0; foreach ($k in 'รับแจ้ง','กำลังส่ง','พักการส่ง','เสร็จงานแล้ว','ยกเลิก') { $sum += $cards[$k] }
Assert "ผลรวม 5 สถานะ = การ์ดทั้งหมด ($sum = $($cards['ทั้งหมด']))" ($sum -eq $cards['ทั้งหมด'])

# ---------------------------------------------------------------- 6.2
Write-Host ''
Write-Host '=== 6.2 ปุ่ม "เดือนนี้" — ตารางรายวันต้องออกครบทุกวัน ===' -ForegroundColor Cyan
$month = Invoke-WebRequest "$BaseUrl/Reports?dateFrom=$monthStart&dateTo=$today" -WebSession $m -UseBasicParsing
$dayRows = [regex]::Matches($month.Content, '<td>(\d{2}/\d{2}/\d{4})</td>')
$expectedDays = ((Get-Date).Date - (Get-Date -Day 1).Date).Days + 1
Assert "จำนวนแถวรายวัน = จำนวนวันในช่วง ($($dayRows.Count) = $expectedDays)" ($dayRows.Count -eq $expectedDays)
$firstDay = (Get-Date -Day 1).ToString('dd/MM/yyyy')
Assert "มีวันที่ 1 ของเดือน ($firstDay) แม้ไม่มีงาน" ($dayRows.Value -contains "<td>$firstDay</td>")
$zeroRows = [regex]::Matches($month.Content, '<tr class="text-body-secondary">\s*<td>\d{2}/\d{2}/\d{4}</td>\s*<td class="text-end fw-semibold">0</td>')
Assert "มีวันที่ไม่มีงานแสดงเป็น 0 จริง ($($zeroRows.Count) วัน)" ($zeroRows.Count -gt 0)

# ---------------------------------------------------------------- 6.3
Write-Host ''
Write-Host '=== 6.3 ตาราง "แยกตามเจ้าหน้าที่ Messenger" ===' -ForegroundColor Cyan
Assert 'มีตารางแยกตาม Messenger' ($month.Content -match 'แยกตามเจ้าหน้าที่ Messenger')
$q = "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.tblDeliveryRequest r INNER JOIN dbo.tblMessengerAssignment a ON a.ReqId=r.ReqId WHERE r.BranchCode='SDC' AND r.SendDate BETWEEN '$monthStart' AND '$today';"
$dbCount = [int](sqlcmd -S localhost -d MessengerDb -E -f 65001 -Q $q -h -1 -W | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1)
# ยอดรวมแถวสุดท้ายของตาราง Messenger
$msgSection = if ($month.Content -match '(?s)แยกตามเจ้าหน้าที่ Messenger(.*?)แยกรายวัน') { $Matches[1] } else { '' }
# แต่ละแถว = 1 เจ้าหน้าที่ · ช่อง text-end ช่องแรกของแถวคือคอลัมน์ "ทั้งหมด"
$msgSum = 0
foreach ($tr in [regex]::Matches($msgSection, '(?s)<tr>(.*?)</tr>')) {
    $tds = [regex]::Matches($tr.Groups[1].Value, '<td class="text-end">(\d+)</td>')
    if ($tds.Count -gt 0) { $msgSum += [int]$tds[0].Groups[1].Value }
}
Assert "ยอดรวมในตาราง ($msgSum) = จำนวนใบที่ยืนยันรับงานแล้วใน DB ($dbCount)" ($msgSum -eq $dbCount)

# ---------------------------------------------------------------- 6.4 / 6.5
Write-Host ''
Write-Host '=== 6.4 / 6.5 ดาวน์โหลด Excel ===' -ForegroundColor Cyan
$xlsx = Join-Path $tmp 'report.xlsx'
Invoke-WebRequest "$BaseUrl/Reports/Export?dateFrom=$monthStart&dateTo=$today" -WebSession $m -UseBasicParsing -OutFile $xlsx
Assert "ดาวน์โหลดได้ ($('{0:N0}' -f (Get-Item $xlsx).Length) bytes)" ((Get-Item $xlsx).Length -gt 0)

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($xlsx)
function Read-Entry($name) {
    $e = $zip.Entries | Where-Object { $_.FullName -eq $name }
    if (-not $e) { return '' }
    $sr = New-Object IO.StreamReader($e.Open()); $t = $sr.ReadToEnd(); $sr.Close(); return $t
}
$wb = Read-Entry 'xl/workbook.xml'
$shared = Read-Entry 'xl/sharedStrings.xml'
$sheet1 = Read-Entry 'xl/worksheets/sheet1.xml'

# ClosedXML เขียน XML โดยมี namespace prefix "x:" ทุก element — regex ต้องเผื่อไว้
$names = ([regex]::Matches($wb, '<(?:x:)?sheet name="([^"]+)"')).ForEach({ $_.Groups[1].Value })
Assert ("มี 2 ชีต : " + ($names -join ', ')) ($names.Count -eq 2 -and $names[0] -eq 'รายการใบงาน' -and $names[1] -eq 'สรุป')
Assert 'ชื่อชีตภาษาไทยไม่เพี้ยน' ($names -contains 'รายการใบงาน')
Assert 'เนื้อหาภาษาไทยไม่เพี้ยน (เจอคำว่า "เลขใบงาน")' ($shared -match 'เลขใบงาน')
Assert 'ไม่มีอักขระเพี้ยนแบบ mojibake' ($shared -notmatch 'à¸|เธ')

# หัวตารางอยู่แถว 4 · ข้อมูลเริ่มแถว 5
$dataRowIds = ([regex]::Matches($sheet1, '<(?:x:)?row r="(\d+)"')).ForEach({ [int]$_.Groups[1].Value }) | Where-Object { $_ -ge 5 }
$dataRows = $dataRowIds.Count
Assert "อ่านแถวข้อมูลจากชีตได้ ($dataRows แถว)" ($dataRows -gt 0)

# วันที่ต้องเป็นตัวเลข (serial) ไม่ใช่ข้อความ จึงจะเรียงลำดับได้
$dateCells = @([regex]::Matches($sheet1, '<(?:x:)?c r="C(\d+)"([^>]*)>') | Where-Object { [int]$_.Groups[1].Value -ge 5 })
$dateAsText = @($dateCells | Where-Object { $_.Groups[2].Value -match 't="s"' })
Assert "คอลัมน์วันที่ส่งเก็บเป็นตัวเลขวันที่ ไม่ใช่ข้อความ (ตรวจ $($dateCells.Count) เซลล์ · เป็นข้อความ $($dateAsText.Count))" ($dateCells.Count -gt 0 -and $dateAsText.Count -eq 0)

# เบอร์โทรต้องเป็นข้อความ ไม่งั้นเลข 0 นำหน้าหาย
$phoneCells = @([regex]::Matches($sheet1, '<(?:x:)?c r="L(\d+)"([^>]*)>') | Where-Object { [int]$_.Groups[1].Value -ge 5 })
$phoneNumeric = @($phoneCells | Where-Object { $_.Groups[2].Value -notmatch 't="s"' -and $_.Groups[2].Value -notmatch 't="inlineStr"' -and $_.Groups[2].Value -notmatch 't="str"' })
Assert "คอลัมน์เบอร์โทรเก็บเป็นข้อความ (ตรวจ $($phoneCells.Count) เซลล์ · ไม่ใช่ข้อความ $($phoneNumeric.Count))" ($phoneCells.Count -gt 0 -and $phoneNumeric.Count -eq 0)
Assert 'เบอร์ที่ขึ้นต้นด้วย 0 ยังอยู่ครบใน sharedStrings' ($shared -match '>0\d{8,9}<')
$zip.Dispose()
$monthCards = Get-Cards $month.Content
Assert "6.5 จำนวนแถวในชีต ($dataRows) = การ์ด 'ทั้งหมด' ($($monthCards['ทั้งหมด']))" ($dataRows -eq $monthCards['ทั้งหมด'])

# ---------------------------------------------------------------- 6.6
Write-Host ''
Write-Host '=== 6.6 User เปิดหน้ารายงาน (ขอบเขตข้อมูลแคบกว่า) ===' -ForegroundColor Cyan
$repU = Invoke-WebRequest "$BaseUrl/Reports?dateFrom=$monthStart&dateTo=$today" -WebSession $u -UseBasicParsing
Assert 'ขึ้นว่า "เฉพาะใบที่คุณเป็นผู้แจ้ง"' ($repU.Content -match 'เฉพาะใบที่คุณเป็นผู้แจ้ง')
Assert 'ไม่ได้ขึ้นว่า "ทั้งสาขา"' ($repU.Content -notmatch 'ทั้งสาขา')
$cardsU = Get-Cards $repU.Content
Assert "ตัวเลขน้อยกว่าของ Messenger (User $($cardsU['ทั้งหมด']) < Messenger $($monthCards['ทั้งหมด']))" ($cardsU['ทั้งหมด'] -lt $monthCards['ทั้งหมด'])

exit (Complete-TestRun $sinceReqId -KeepData:$KeepData)
