/* =============================================================
   040_SeedData.sql
   Seed ข้อมูลตั้งต้น : สาขา SDC/SBK + พนักงาน mock สำหรับ SSO stub
   -------------------------------------------------------------
   - รันซ้ำได้ (idempotent) — insert เฉพาะแถวที่ยังไม่มี
   - พนักงานชุดนี้เป็นข้อมูล "mock SSO" ของ Phase 0 เท่านั้น
     เมื่อได้ contract SSO จริง (D3) ให้เปลี่ยนไปดึงจาก SSO แทน
   ============================================================= */

USE [MessengerDb];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ---------- สาขา ---------- */
IF NOT EXISTS (SELECT 1 FROM dbo.tblBranch WHERE BranchCode = 'SDC')
    INSERT INTO dbo.tblBranch (BranchCode, BranchName) VALUES ('SDC', N'สำนักงานสาขา SDC');

IF NOT EXISTS (SELECT 1 FROM dbo.tblBranch WHERE BranchCode = 'SBK')
    INSERT INTO dbo.tblBranch (BranchCode, BranchName) VALUES ('SBK', N'สำนักงานสาขา SBK');
GO

/* ---------- พนักงาน mock ----------
   สาขา SDC = รหัส 1xxxx  /  สาขา SBK = รหัส 2xxxx
   ครบทั้ง 3 role ในทั้ง 2 สาขา เพื่อทดสอบ BR-6 branch isolation
   ---------------------------------- */
DECLARE @Emp TABLE
(
    EmpCode    VARCHAR(20),
    FullName   NVARCHAR(200),
    DeptCode   VARCHAR(20),
    UnitName   NVARCHAR(200),
    PhoneExt   VARCHAR(20),
    Email      NVARCHAR(200),
    BranchCode CHAR(3),
    RoleCode   CHAR(1)
);

INSERT INTO @Emp (EmpCode, FullName, DeptCode, UnitName, PhoneExt, Email, BranchCode, RoleCode)
VALUES
    -- สาขา SDC
    ('10001', N'สมชาย ใจดี',      'IT',  N'ฝ่ายเทคโนโลยีสารสนเทศ', '1101', N'somchai@example.co.th',  'SDC', 'A'),
    ('10002', N'สมหญิง รักงาน',   'ACC', N'ฝ่ายบัญชีและการเงิน',   '1201', N'somying@example.co.th',  'SDC', 'U'),
    ('10003', N'ประเสริฐ ว่องไว', 'ADM', N'ฝ่ายธุรการ',            '1301', N'prasert@example.co.th',  'SDC', 'M'),
    ('10004', N'อารีย์ พากเพียร', 'HR',  N'ฝ่ายทรัพยากรบุคคล',     '1401', N'aree@example.co.th',     'SDC', 'U'),
    -- สาขา SBK
    ('20001', N'วิชัย มั่นคง',     'IT',  N'ฝ่ายเทคโนโลยีสารสนเทศ', '2101', N'wichai@example.co.th',   'SBK', 'A'),
    ('20002', N'นภา สดใส',        'ACC', N'ฝ่ายบัญชีและการเงิน',   '2201', N'napa@example.co.th',     'SBK', 'U'),
    ('20003', N'ธนา เร็วรี่',      'ADM', N'ฝ่ายธุรการ',            '2301', N'thana@example.co.th',    'SBK', 'M'),
    ('20004', N'ชูใจ ตั้งใจ',      'HR',  N'ฝ่ายทรัพยากรบุคคล',     '2401', N'chujai@example.co.th',   'SBK', 'U');

INSERT INTO dbo.tblEmployee (EmpCode, FullName, DeptCode, UnitName, PhoneExt, Email, BranchCode)
SELECT s.EmpCode, s.FullName, s.DeptCode, s.UnitName, s.PhoneExt, s.Email, s.BranchCode
FROM @Emp AS s
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblEmployee AS e WHERE e.EmpCode = s.EmpCode);

INSERT INTO dbo.tblUserRole (EmpCode, BranchCode, RoleCode, CreatedBy)
SELECT s.EmpCode, s.BranchCode, s.RoleCode, N'SEED'
FROM @Emp AS s
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblUserRole AS r WHERE r.EmpCode = s.EmpCode);
GO

PRINT '--- 040_SeedData.sql completed ---';

SELECT BranchCode, BranchName FROM dbo.tblBranch ORDER BY BranchCode;
SELECT EmpCode, FullName, BranchCode, RoleCode FROM dbo.vwEmployeeRole ORDER BY BranchCode, RoleCode, EmpCode;
GO
