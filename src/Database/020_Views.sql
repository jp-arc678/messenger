/* =============================================================
   020_Views.sql
   Views ที่ใช้ร่วมกัน (data access ผ่าน View + Stored Procedure)
   -------------------------------------------------------------
   - รันซ้ำได้ : DROP แล้ว CREATE ใหม่
   - ไม่ใช้ CREATE OR ALTER เพราะ production เป็น SQL Server 2014
   ============================================================= */

USE [MessengerDb];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ---------- vwEmployeeRole ----------
   พนักงาน + สาขา + role ที่ resolve แล้ว
   D10 : คนที่ยังไม่มีแถวใน tblUserRole ให้ถือเป็น 'U' (User) เสมอ
   ------------------------------------ */
IF OBJECT_ID(N'dbo.vwEmployeeRole', N'V') IS NOT NULL
    DROP VIEW dbo.vwEmployeeRole;
GO

CREATE VIEW dbo.vwEmployeeRole
AS
SELECT
    e.EmpCode,
    e.FullName,
    e.DeptCode,
    e.UnitName,
    e.PhoneExt,
    e.Email,
    e.BranchCode,
    b.BranchName,
    e.IsActive,
    ISNULL(ur.RoleCode, 'U') AS RoleCode
FROM dbo.tblEmployee AS e
INNER JOIN dbo.tblBranch AS b
        ON b.BranchCode = e.BranchCode
LEFT JOIN dbo.tblUserRole AS ur
        ON ur.EmpCode = e.EmpCode;
GO

/* ---------- vwDeliveryRequest ----------
   ใบแจ้งงาน + ชื่อสาขา + ข้อมูลผู้แจ้ง + การรับงานของ Messenger (join ไว้ให้แล้ว)
   ทุก query ที่ใช้ view นี้ "ต้อง" มี WHERE BranchCode เสมอ ตาม BR-6

   ส่วนของ assignment เป็น LEFT JOIN เพราะใบงานสถานะ Received ยังไม่มีคนรับงาน
   คอลัมน์ SequenceOrder / MessengerEmpCode / ConfirmedAt จึงเป็น NULL ได้
   --------------------------------------- */
IF OBJECT_ID(N'dbo.vwDeliveryRequest', N'V') IS NOT NULL
    DROP VIEW dbo.vwDeliveryRequest;
GO

CREATE VIEW dbo.vwDeliveryRequest
AS
SELECT
    r.ReqId,
    r.ReqNo,
    r.BranchCode,
    b.BranchName,
    r.RequesterEmpCode,
    e.FullName   AS RequesterName,
    e.DeptCode   AS RequesterDeptCode,
    e.UnitName   AS RequesterUnitName,
    e.PhoneExt   AS RequesterPhoneExt,
    e.Email      AS RequesterEmail,
    r.RequestDateTime,
    r.SendDate,
    r.ContactName,
    r.Address,
    r.Phone,
    r.Detail,
    r.Status,
    r.IsPersonal,
    r.ReceiptConfirmed,
    r.ReceiptConfirmedAt,
    r.ReceiptConfirmedBy,
    r.[RowVersion],
    r.CreatedBy,
    r.CreatedAt,
    r.UpdatedBy,
    r.UpdatedAt,
    a.MessengerEmpCode,
    m.FullName   AS MessengerName,
    a.ConfirmedAt,
    a.SequenceOrder,
    a.Route,
    a.DistanceKm,
    a.ReturnToOffice
FROM dbo.tblDeliveryRequest AS r
INNER JOIN dbo.tblBranch AS b
        ON b.BranchCode = r.BranchCode
INNER JOIN dbo.tblEmployee AS e
        ON e.EmpCode = r.RequesterEmpCode
LEFT JOIN dbo.tblMessengerAssignment AS a
        ON a.ReqId = r.ReqId
LEFT JOIN dbo.tblEmployee AS m
        ON m.EmpCode = a.MessengerEmpCode;
GO

PRINT '--- 020_Views.sql completed ---';
GO
