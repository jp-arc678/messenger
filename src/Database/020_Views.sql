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

/* ---------- vw_EmployeeRole ----------
   พนักงาน + สาขา + role ที่ resolve แล้ว
   D10 : คนที่ยังไม่มีแถวใน UserRole ให้ถือเป็น 'U' (User) เสมอ
   ------------------------------------- */
IF OBJECT_ID(N'dbo.vw_EmployeeRole', N'V') IS NOT NULL
    DROP VIEW dbo.vw_EmployeeRole;
GO

CREATE VIEW dbo.vw_EmployeeRole
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
FROM dbo.Employee AS e
INNER JOIN dbo.Branch AS b
        ON b.BranchCode = e.BranchCode
LEFT JOIN dbo.UserRole AS ur
        ON ur.EmpCode = e.EmpCode;
GO

PRINT '--- 020_Views.sql completed ---';
GO
