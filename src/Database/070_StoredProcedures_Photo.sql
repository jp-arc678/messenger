/* =============================================================
   070_StoredProcedures_Photo.sql
   Stored procedures ของ Phase 3 : รูปยืนยัน (BR-3) + ยืนยันรับของ (BR-4)
   -------------------------------------------------------------
   - รันซ้ำได้ : DROP แล้ว CREATE ใหม่
   - เข้ากับ SQL Server 2014 (compat 120) — ห้ามใช้ CREATE OR ALTER
   - ทุก procedure รับ @BranchCode และ join กับ tblDeliveryRequest เพื่อบังคับ
     BR-6 ที่ระดับ DB : รูปของใบงานสาขาอื่นต้องมองไม่เห็นและลบไม่ได้
     แม้จะเดา PhotoId ถูกก็ตาม

   ตัวไฟล์รูปอยู่บน filesystem — ตารางนี้เก็บแค่ path (BR-3)
   ============================================================= */

USE [MessengerDb];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* =============================================================
   spDeliveryPhotoAdd — บันทึกข้อมูลรูป 1 ใบ

   @PhotoId = 0 หมายถึงใบงานไม่ได้อยู่ในสาขานี้ (ไม่ได้ insert อะไรเลย)
   ============================================================= */
IF OBJECT_ID(N'dbo.spDeliveryPhotoAdd', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spDeliveryPhotoAdd;
GO

CREATE PROCEDURE dbo.spDeliveryPhotoAdd
    @ReqId          INT,
    @BranchCode     CHAR(3),
    @PhotoType      VARCHAR(10),
    @FilePath       NVARCHAR(500),
    @FileName       NVARCHAR(255) = NULL,
    @FileSizeBytes  INT           = NULL,
    @CapturedAt     DATETIME2(0),
    @CapturedBy     VARCHAR(20),
    @PhotoId        INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SET @PhotoId = 0;

    IF NOT EXISTS (SELECT 1 FROM dbo.tblDeliveryRequest
                   WHERE ReqId = @ReqId AND BranchCode = @BranchCode)
        RETURN;

    INSERT INTO dbo.tblDeliveryPhoto (ReqId, PhotoType, FilePath, FileName, FileSizeBytes, CapturedAt, CapturedBy)
    VALUES (@ReqId, @PhotoType, @FilePath, @FileName, @FileSizeBytes, @CapturedAt, @CapturedBy);

    SET @PhotoId = CAST(SCOPE_IDENTITY() AS INT);
END
GO

/* =============================================================
   spDeliveryPhotoListByReq — รูปทั้งหมดของใบงาน (เก่า → ใหม่)
   ============================================================= */
IF OBJECT_ID(N'dbo.spDeliveryPhotoListByReq', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spDeliveryPhotoListByReq;
GO

CREATE PROCEDURE dbo.spDeliveryPhotoListByReq
    @ReqId      INT,
    @BranchCode CHAR(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.PhotoId,
           p.ReqId,
           p.PhotoType,
           p.FilePath,
           p.FileName,
           p.FileSizeBytes,
           p.CapturedAt,
           p.CapturedBy,
           e.FullName AS CapturedByName
    FROM dbo.tblDeliveryPhoto AS p
    INNER JOIN dbo.tblDeliveryRequest AS r
            ON r.ReqId = p.ReqId
    LEFT JOIN dbo.tblEmployee AS e
            ON e.EmpCode = p.CapturedBy
    WHERE p.ReqId      = @ReqId
      AND r.BranchCode = @BranchCode
    ORDER BY p.CapturedAt, p.PhotoId;
END
GO

/* =============================================================
   spDeliveryPhotoGetById — รูป 1 ใบ (ใช้ตอนเสิร์ฟไฟล์ออกหน้าจอและตอนลบ)
   ============================================================= */
IF OBJECT_ID(N'dbo.spDeliveryPhotoGetById', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spDeliveryPhotoGetById;
GO

CREATE PROCEDURE dbo.spDeliveryPhotoGetById
    @PhotoId    INT,
    @BranchCode CHAR(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.PhotoId,
           p.ReqId,
           p.PhotoType,
           p.FilePath,
           p.FileName,
           p.FileSizeBytes,
           p.CapturedAt,
           p.CapturedBy,
           e.FullName AS CapturedByName
    FROM dbo.tblDeliveryPhoto AS p
    INNER JOIN dbo.tblDeliveryRequest AS r
            ON r.ReqId = p.ReqId
    LEFT JOIN dbo.tblEmployee AS e
            ON e.EmpCode = p.CapturedBy
    WHERE p.PhotoId    = @PhotoId
      AND r.BranchCode = @BranchCode;
END
GO

/* =============================================================
   spDeliveryPhotoCountByReq — จำนวนรูปของใบงาน (ใช้จำกัดจำนวนต่อใบ)
   ============================================================= */
IF OBJECT_ID(N'dbo.spDeliveryPhotoCountByReq', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spDeliveryPhotoCountByReq;
GO

CREATE PROCEDURE dbo.spDeliveryPhotoCountByReq
    @ReqId      INT,
    @BranchCode CHAR(3),
    @PhotoCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @PhotoCount = COUNT(*)
    FROM dbo.tblDeliveryPhoto AS p
    INNER JOIN dbo.tblDeliveryRequest AS r
            ON r.ReqId = p.ReqId
    WHERE p.ReqId      = @ReqId
      AND r.BranchCode = @BranchCode;
END
GO

/* =============================================================
   spDeliveryPhotoDelete — ลบข้อมูลรูป (D24)

   ลบเฉพาะแถวใน DB — ไฟล์จริงถูกลบโดย service layer หลังจากนี้
   procedure ไม่ตรวจสถานะ/สิทธิ์ เป็นหน้าที่ของ service (§10 ข้อ 6)
   ============================================================= */
IF OBJECT_ID(N'dbo.spDeliveryPhotoDelete', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spDeliveryPhotoDelete;
GO

CREATE PROCEDURE dbo.spDeliveryPhotoDelete
    @PhotoId        INT,
    @BranchCode     CHAR(3),
    @RowsAffected   INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE p
    FROM dbo.tblDeliveryPhoto AS p
    INNER JOIN dbo.tblDeliveryRequest AS r
            ON r.ReqId = p.ReqId
    WHERE p.PhotoId    = @PhotoId
      AND r.BranchCode = @BranchCode;

    SET @RowsAffected = @@ROWCOUNT;
END
GO

/* =============================================================
   spDeliveryRequestConfirmReceipt — กดยืนยันว่ารับของกลับมาแล้ว (BR-4)

   เงื่อนไข ReceiptConfirmed = 0 ใน WHERE ทำให้กดซ้ำไม่ได้ :
   คนที่สองจะได้ @RowsAffected = 0 แทนที่จะไปทับเวลา/ชื่อคนแรก

   ไม่ลง tblStatusHistory เพราะไม่ใช่การเปลี่ยนสถานะตาม §6
   ตัว audit อยู่ที่คอลัมน์ ReceiptConfirmedAt/By ของใบงานเอง
   ============================================================= */
IF OBJECT_ID(N'dbo.spDeliveryRequestConfirmReceipt', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spDeliveryRequestConfirmReceipt;
GO

CREATE PROCEDURE dbo.spDeliveryRequestConfirmReceipt
    @ReqId          INT,
    @BranchCode     CHAR(3),
    @ByEmpCode      VARCHAR(20),
    @ConfirmedAt    DATETIME2(0),
    @RowsAffected   INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.tblDeliveryRequest
    SET ReceiptConfirmed   = 1,
        ReceiptConfirmedAt = @ConfirmedAt,
        ReceiptConfirmedBy = @ByEmpCode
    WHERE ReqId            = @ReqId
      AND BranchCode       = @BranchCode
      AND ReceiptConfirmed = 0;

    SET @RowsAffected = @@ROWCOUNT;
END
GO

PRINT '--- 070_StoredProcedures_Photo.sql completed ---';
GO
