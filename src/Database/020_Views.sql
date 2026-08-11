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

PRINT '--- 020_Views.sql completed ---';
GO
