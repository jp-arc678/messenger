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

Invoke-Sql 'การรับงานของ Messenger + ลำดับวิ่งงาน (D11)' @'
SELECT a.ReqId, r.ReqNo, r.BranchCode,
       CONVERT(varchar(10), r.SendDate, 120) AS SendDate,
       a.SequenceOrder, a.MessengerEmpCode,
       CONVERT(varchar(16), a.ConfirmedAt, 120) AS ConfirmedAt
FROM MessengerDb.dbo.tblMessengerAssignment AS a
INNER JOIN MessengerDb.dbo.tblDeliveryRequest AS r ON r.ReqId = a.ReqId
ORDER BY r.BranchCode, r.SendDate, a.SequenceOrder;
'@

Invoke-Sql 'ประวัติการเปลี่ยนสถานะทั้งหมด (audit trail ตาม §6)' @'
SELECT ReqId, ISNULL(FromStatus, '(สร้างใหม่)') AS FromStatus, ToStatus, ByEmpCode,
       CONVERT(varchar(16), ChangedAt, 120) AS ChangedAt, Note
FROM MessengerDb.dbo.tblStatusHistory
ORDER BY ReqId, HistoryId;
'@

Invoke-Sql 'เหตุผลการพัก / การยกเลิก' @'
SELECT 'Pause' AS Kind, ReqId, ByEmpCode, Reason FROM MessengerDb.dbo.tblPauseReason
UNION ALL
SELECT 'Cancel', ReqId, ByEmpCode, Reason FROM MessengerDb.dbo.tblCancelReason
ORDER BY ReqId;
'@

Invoke-Sql 'รูปยืนยัน (BR-3) — DB เก็บแค่ path ไฟล์จริงอยู่บนดิสก์' @'
SELECT p.PhotoId, r.ReqNo, p.PhotoType, p.FileSizeBytes, p.FilePath,
       CONVERT(varchar(16), p.CapturedAt, 120) AS CapturedAt, p.CapturedBy
FROM MessengerDb.dbo.tblDeliveryPhoto AS p
INNER JOIN MessengerDb.dbo.tblDeliveryRequest AS r ON r.ReqId = p.ReqId
ORDER BY p.PhotoId;
'@

Invoke-Sql 'ใบที่ต้องยืนยันรับของ (BR-4) และสถานะการยืนยัน' @'
SELECT r.ReqId, r.ReqNo, r.Status, r.ReceiptConfirmed,
       CONVERT(varchar(16), r.ReceiptConfirmedAt, 120) AS ReceiptConfirmedAt,
       r.ReceiptConfirmedBy
FROM MessengerDb.dbo.tblDeliveryRequest AS r
WHERE EXISTS (SELECT 1 FROM MessengerDb.dbo.tblRequestJobType AS j
              WHERE j.ReqId = r.ReqId AND j.JobType = 'ReceiveDoc')
ORDER BY r.ReqId;
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
