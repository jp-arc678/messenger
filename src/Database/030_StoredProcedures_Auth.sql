/* =============================================================
   030_StoredProcedures_Auth.sql
   Stored procedures ของ Phase 0 : Auth / SSO / Branch / Role
   -------------------------------------------------------------
   - รันซ้ำได้ : DROP แล้ว CREATE ใหม่
   - ไม่ใช้ CREATE OR ALTER เพราะ production เป็น SQL Server 2014
   ============================================================= */

USE [MessengerDb];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* =============================================================
   spBranchList — รายชื่อสาขาที่ยังใช้งาน
   ============================================================= */
IF OBJECT_ID(N'dbo.spBranchList', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spBranchList;
GO

CREATE PROCEDURE dbo.spBranchList
AS
BEGIN
    SET NOCOUNT ON;

    SELECT BranchCode, BranchName, IsActive
    FROM dbo.tblBranch
    WHERE IsActive = 1
    ORDER BY BranchCode;
END
GO

/* =============================================================
   spEmployeeGetByEmpCode — ดึงพนักงาน + role ที่ resolve แล้ว
   ใช้ตอน login และตอนสร้าง principal ในแต่ละ request
   ============================================================= */
IF OBJECT_ID(N'dbo.spEmployeeGetByEmpCode', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spEmployeeGetByEmpCode;
GO

CREATE PROCEDURE dbo.spEmployeeGetByEmpCode
    @EmpCode VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT EmpCode, FullName, DeptCode, UnitName, PhoneExt, Email,
           BranchCode, BranchName, RoleCode, IsActive
    FROM dbo.vwEmployeeRole
    WHERE EmpCode = @EmpCode;
END
GO

/* =============================================================
   spEmployeeListByBranch — รายชื่อพนักงาน (ใช้กับหน้า mock login,
   การเลือกผู้แจ้งแทนคนอื่นตาม D17 และ master data ภายหลัง)
   @BranchCode = NULL คือทุกสาขา
   ============================================================= */
IF OBJECT_ID(N'dbo.spEmployeeListByBranch', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spEmployeeListByBranch;
GO

CREATE PROCEDURE dbo.spEmployeeListByBranch
    @BranchCode CHAR(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT EmpCode, FullName, DeptCode, UnitName, PhoneExt, Email,
           BranchCode, BranchName, RoleCode, IsActive
    FROM dbo.vwEmployeeRole
    WHERE IsActive = 1
      AND (@BranchCode IS NULL OR BranchCode = @BranchCode)
    ORDER BY BranchCode, RoleCode, EmpCode;
END
GO

/* =============================================================
   spEmployeeUpsertFromSso — cache ข้อมูลพนักงานจาก SSO (BR-7)
   เรียกทุกครั้งที่ login สำเร็จ
   D10 : คนใหม่ที่ยังไม่มี role → ใส่แถว tblUserRole เป็น 'U' ให้ทันที
   หมายเหตุ : ไม่แตะ RoleCode ของคนที่มีอยู่แล้ว (role มาจากระบบเรา
              ไม่ได้มาจาก SSO)
   ============================================================= */
IF OBJECT_ID(N'dbo.spEmployeeUpsertFromSso', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spEmployeeUpsertFromSso;
GO

CREATE PROCEDURE dbo.spEmployeeUpsertFromSso
    @EmpCode    VARCHAR(20),
    @FullName   NVARCHAR(200),
    @DeptCode   VARCHAR(20)   = NULL,
    @UnitName   NVARCHAR(200) = NULL,
    @PhoneExt   VARCHAR(20)   = NULL,
    @Email      NVARCHAR(200) = NULL,
    @BranchCode CHAR(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM dbo.tblEmployee WHERE EmpCode = @EmpCode)
    BEGIN
        UPDATE dbo.tblEmployee
        SET FullName   = @FullName,
            DeptCode   = @DeptCode,
            UnitName   = @UnitName,
            PhoneExt   = @PhoneExt,
            Email      = @Email,
            BranchCode = @BranchCode,
            SyncedAt   = SYSDATETIME(),
            UpdatedAt  = SYSDATETIME()
        WHERE EmpCode = @EmpCode;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.tblEmployee (EmpCode, FullName, DeptCode, UnitName, PhoneExt, Email, BranchCode)
        VALUES (@EmpCode, @FullName, @DeptCode, @UnitName, @PhoneExt, @Email, @BranchCode);
    END

    -- คนใหม่เริ่มที่ U-User เสมอ (D10)
    IF NOT EXISTS (SELECT 1 FROM dbo.tblUserRole WHERE EmpCode = @EmpCode)
    BEGIN
        INSERT INTO dbo.tblUserRole (EmpCode, BranchCode, RoleCode, CreatedBy)
        VALUES (@EmpCode, @BranchCode, 'U', N'SSO');
    END
    ELSE
    BEGIN
        -- ย้ายสาขา : sync BranchCode ของ role ให้ตรงกับ SSO เสมอ (BR-6)
        UPDATE dbo.tblUserRole
        SET BranchCode = @BranchCode,
            UpdatedBy  = N'SSO',
            UpdatedAt  = SYSDATETIME()
        WHERE EmpCode = @EmpCode
          AND BranchCode <> @BranchCode;
    END

    COMMIT TRANSACTION;

    -- คืนข้อมูลที่ resolve แล้วกลับไปให้ service layer
    SELECT EmpCode, FullName, DeptCode, UnitName, PhoneExt, Email,
           BranchCode, BranchName, RoleCode, IsActive
    FROM dbo.vwEmployeeRole
    WHERE EmpCode = @EmpCode;
END
GO

PRINT '--- 030_StoredProcedures_Auth.sql completed ---';
GO
