<#
    ตรวจว่าใบแจ้งงานถูกบันทึกลง DB จริงไหม และ stored procedure คืนอะไรกลับมา
    ใช้:  pwsh -File tools\Diag-Requests.ps1
#>

[CmdletBinding()]
param(
    [string] $SqlServer = 'localhost'
)

function Invoke-Sql([string] $title, [string] $query) {
    Write-Host ''
    Write-Host ("=== " + $title + " ===") -ForegroundColor Cyan
    & sqlcmd -S $SqlServer -E -W -s '|' -Q ("SET NOCOUNT ON; " + $query)
}

Invoke-Sql 'ใบแจ้งงานทั้งหมดในตาราง (ดิบ ไม่ผ่าน filter)' @'
SELECT ReqId, ReqNo, BranchCode, RequesterEmpCode, CreatedBy,
       CONVERT(varchar(10), SendDate, 120) AS SendDate,
       CONVERT(varchar(16), RequestDateTime, 120) AS RequestDateTime,
       Status
FROM MessengerDb.dbo.tblDeliveryRequest
ORDER BY ReqId;
'@

Invoke-Sql 'ประเภทงานของแต่ละใบ' @'
SELECT ReqId, JobType, DetailText
FROM MessengerDb.dbo.tblRequestJobType
ORDER BY ReqId, ReqJobTypeId;
'@

Invoke-Sql 'เลขลำดับที่ระบบจองไว้ (BR-8)' @'
SELECT BranchCode, YyMm, LastNumber
FROM MessengerDb.dbo.tblReqNoSequence
ORDER BY BranchCode, YyMm;
'@

Invoke-Sql 'ผลลัพธ์จริงของ spDeliveryRequestList : สาขา SDC ทั้งสาขา' @'
EXEC MessengerDb.dbo.spDeliveryRequestList @BranchCode = 'SDC';
'@

Invoke-Sql 'ผลลัพธ์จริงของ spDeliveryRequestList : สาขา SBK ทั้งสาขา' @'
EXEC MessengerDb.dbo.spDeliveryRequestList @BranchCode = 'SBK';
'@

Write-Host ''
Write-Host 'อ่านผลอย่างไร:' -ForegroundColor Yellow
Write-Host '  - ตารางแรกว่าง        = ไม่ได้บันทึกลง DB เลย (ปัญหาอยู่ที่ตอนสร้าง)'
Write-Host '  - ตารางแรกมีข้อมูล แต่ EXEC ว่าง = ปัญหาอยู่ที่ stored procedure หรือ view'
Write-Host '  - EXEC มีข้อมูล แต่หน้าเว็บว่าง  = ปัญหาอยู่ที่ตัวกรอง (สาขา/ผู้แจ้ง/ช่วงวันที่)'
