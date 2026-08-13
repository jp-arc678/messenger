/* =============================================================
   050_StoredProcedures_DeliveryRequest.sql
   Stored procedures ของ Phase 1 : ใบแจ้งงาน (สร้าง / แก้ / ดู)
   -------------------------------------------------------------
   - รันซ้ำได้ : DROP แล้ว CREATE ใหม่
   - เข้ากับ SQL Server 2014 (compat 120) — ห้ามใช้ CREATE OR ALTER
   - ทุก procedure ที่อ่าน/เขียนใบงาน ต้องรับ @BranchCode และใช้เป็น
     เงื่อนไขเสมอ เพื่อบังคับ BR-6 ที่ระดับ DB ไม่ใช่แค่ UI
   ============================================================= */

USE [MessengerDb];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* =============================================================
   spReqNoNext — ขอเลขลำดับถัดไปของ (สาขา + YYMM) ตาม BR-8

   ต้องถูกเรียก "ภายใน transaction ของผู้เรียก" เสมอ เพื่อให้เลขที่ได้
   ถูกจองไว้จนกว่าใบงานจะถูก insert สำเร็จ

   UPDLOCK + SERIALIZABLE คือหัวใจของการกันเลขซ้ำ :
   - UPDLOCK      กันสองคนอ่านค่าเดิมพร้อมกันแล้วบวกทับกัน
   - SERIALIZABLE ล็อก key range ไว้ ทำให้กรณีที่ยังไม่มีแถวของเดือนนี้
                  จะมีแค่ session เดียวที่ INSERT ได้
   ============================================================= */
IF OBJECT_ID(N'dbo.spReqNoNext', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spReqNoNext;
GO

CREATE PROCEDURE dbo.spReqNoNext
    @BranchCode CHAR(3),
    @YyMm       CHAR(4),
    @NextNumber INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SET @NextNumber = NULL;

    UPDATE dbo.tblReqNoSequence WITH (UPDLOCK, SERIALIZABLE)
    SET LastNumber  = LastNumber + 1,
        UpdatedAt   = SYSDATETIME(),
        @NextNumber = LastNumber + 1
    WHERE BranchCode = @BranchCode
      AND YyMm       = @YyMm;

    -- ยังไม่มีแถวของเดือนนี้ = ใบแรกของเดือน (BR-8 reset ทุกเดือน)
    IF @NextNumber IS NULL
    BEGIN
        INSERT INTO dbo.tblReqNoSequence (BranchCode, YyMm, LastNumber)
        VALUES (@BranchCode, @YyMm, 1);

        SET @NextNumber = 1;
    END
END
GO

/* =============================================================
   spDeliveryRequestCreate — สร้างใบแจ้งงานใหม่

   สถานะเริ่มต้นเป็น 'Received' เสมอ ตาม §6 และบันทึก tblStatusHistory
   ด้วย FromStatus = NULL เพื่อให้ audit trail ครบตั้งแต่ใบแรก

   @ReqNo ถูกคำนวณจาก service layer (BR-8) แล้วส่งเข้ามา — procedure นี้
   ไม่ประกอบเลขเอง เพื่อให้กฎรูปแบบเลขใบงานมีที่อยู่เดียวและทดสอบได้
   ============================================================= */
IF OBJECT_ID(N'dbo.spDeliveryRequestCreate', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spDeliveryRequestCreate;
GO

CREATE PROCEDURE dbo.spDeliveryRequestCreate
    @ReqNo              VARCHAR(20),
    @BranchCode         CHAR(3),
    @RequesterEmpCode   VARCHAR(20),
    @RequestDateTime    DATETIME2(0),
    @SendDate           DATE,
    @ContactName        NVARCHAR(200),
    @Address            NVARCHAR(1000),
    @Phone              VARCHAR(50)    = NULL,
    @Detail             NVARCHAR(MAX),
    @IsPersonal         BIT,
    @CreatedBy          VARCHAR(20),
    @ReqId              INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.tblDeliveryRequest
    (
        ReqNo, BranchCode, RequesterEmpCode, RequestDateTime, SendDate,
        ContactName, Address, Phone, Detail, Status, IsPersonal, CreatedBy
    )
    VALUES
    (
        @ReqNo, @BranchCode, @RequesterEmpCode, @RequestDateTime, @SendDate,
        @ContactName, @Address, @Phone, @Detail, 'Received', @IsPersonal, @CreatedBy
    );

    SET @ReqId = CAST(SCOPE_IDENTITY() AS INT);

    INSERT INTO dbo.tblStatusHistory (ReqId, FromStatus, ToStatus, ByEmpCode, Note)
    VALUES (@ReqId, NULL, 'Received', @CreatedBy, N'สร้างใบแจ้งงาน');
END
GO

/* =============================================================
   spRequestJobTypeAdd — เพิ่มประเภทงาน 1 รายการให้ใบงาน
   (1 ใบมีได้หลายประเภท จึงเรียกซ้ำได้ตามจำนวนที่ผู้ใช้ติ๊ก)
   ============================================================= */
IF OBJECT_ID(N'dbo.spRequestJobTypeAdd', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spRequestJobTypeAdd;
GO

CREATE PROCEDURE dbo.spRequestJobTypeAdd
    @ReqId      INT,
    @JobType    VARCHAR(20),
    @DetailText NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.tblRequestJobType (ReqId, JobType, DetailText)
    VALUES (@ReqId, @JobType, @DetailText);
END
GO

/* =============================================================
   spRequestJobTypeClear — ลบประเภทงานทั้งหมดของใบงาน
   ใช้ตอนแก้ไข : ลบของเดิมทิ้งแล้วใส่ชุดใหม่ (ทำใน transaction เดียวกัน)
   ============================================================= */
IF OBJECT_ID(N'dbo.spRequestJobTypeClear', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spRequestJobTypeClear;
GO

CREATE PROCEDURE dbo.spRequestJobTypeClear
    @ReqId INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.tblRequestJobType
    WHERE ReqId = @ReqId;
END
GO

/* =============================================================
   spRequestJobTypeListByReq — ประเภทงานทั้งหมดของใบงานหนึ่งใบ
   ============================================================= */
IF OBJECT_ID(N'dbo.spRequestJobTypeListByReq', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spRequestJobTypeListByReq;
GO

CREATE PROCEDURE dbo.spRequestJobTypeListByReq
    @ReqId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ReqJobTypeId, ReqId, JobType, DetailText
    FROM dbo.tblRequestJobType
    WHERE ReqId = @ReqId
    ORDER BY ReqJobTypeId;
END
GO

/* =============================================================
   spDeliveryRequestUpdate — แก้ไขใบแจ้งงาน (BR-2)

   optimistic locking : ถ้า @RowVersion ไม่ตรงกับค่าปัจจุบัน แปลว่ามีคนอื่น
   แก้ไปแล้วระหว่างที่เรากำลังกรอกฟอร์ม → ไม่มีแถวไหนถูกอัปเดต
   คืน RowsAffected = 0 ให้ service layer แจ้งผู้ใช้

   เงื่อนไข BranchCode อยู่ใน WHERE ด้วยเสมอ (BR-6) แม้ ReqId จะ unique อยู่แล้ว
   เพื่อไม่ให้ใบงานข้ามสาขาถูกแก้ได้แม้จะเดา ReqId ถูก

   หมายเหตุ : procedure นี้ "ไม่" ตรวจสถานะหรือสิทธิ์ — เป็นหน้าที่ของ
   service layer ตาม §10 ข้อ 6
   ============================================================= */
IF OBJECT_ID(N'dbo.spDeliveryRequestUpdate', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spDeliveryRequestUpdate;
GO

CREATE PROCEDURE dbo.spDeliveryRequestUpdate
    @ReqId              INT,
    @BranchCode         CHAR(3),
    @RequesterEmpCode   VARCHAR(20),
    @SendDate           DATE,
    @ContactName        NVARCHAR(200),
    @Address            NVARCHAR(1000),
    @Phone              VARCHAR(50)    = NULL,
    @Detail             NVARCHAR(MAX),
    @IsPersonal         BIT,
    @UpdatedBy          VARCHAR(20),
    @RowVersion         BINARY(8),
    @RowsAffected       INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.tblDeliveryRequest
    SET RequesterEmpCode = @RequesterEmpCode,
        SendDate         = @SendDate,
        ContactName      = @ContactName,
        Address          = @Address,
        Phone            = @Phone,
        Detail           = @Detail,
        IsPersonal       = @IsPersonal,
        UpdatedBy        = @UpdatedBy,
        UpdatedAt        = SYSDATETIME()
    WHERE ReqId        = @ReqId
      AND BranchCode   = @BranchCode
      AND [RowVersion] = @RowVersion;

    SET @RowsAffected = @@ROWCOUNT;
END
GO

/* =============================================================
   spDeliveryRequestGetById — ดูใบงาน 1 ใบ

   @BranchCode บังคับส่งเสมอ : ถ้าใบงานอยู่คนละสาขา จะไม่มีแถวคืนกลับไป
   (BR-6 — เห็นเฉพาะสาขาตัวเอง)
   ============================================================= */
IF OBJECT_ID(N'dbo.spDeliveryRequestGetById', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spDeliveryRequestGetById;
GO

CREATE PROCEDURE dbo.spDeliveryRequestGetById
    @ReqId      INT,
    @BranchCode CHAR(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ReqId, ReqNo, BranchCode, BranchName,
           RequesterEmpCode, RequesterName, RequesterDeptCode, RequesterUnitName,
           RequesterPhoneExt, RequesterEmail,
           RequestDateTime, SendDate, ContactName, Address, Phone, Detail,
           Status, IsPersonal, ReceiptConfirmed, ReceiptConfirmedAt, ReceiptConfirmedBy, [RowVersion],
           CreatedBy, CreatedAt, UpdatedBy, UpdatedAt,
           MessengerEmpCode, MessengerName, ConfirmedAt, SequenceOrder,
           Route, DistanceKm, ReturnToOffice, JobTypeCodes
    FROM dbo.vwDeliveryRequest
    WHERE ReqId      = @ReqId
      AND BranchCode = @BranchCode;
END
GO

/* =============================================================
   spDeliveryRequestList — รายการใบแจ้งงานของสาขา

   @VisibleToEmpCode :
     - ส่งรหัสพนักงาน = เห็นเฉพาะใบที่คนนั้นเกี่ยวข้อง (ใช้กับ U-User ตาม §5)
     - ส่ง NULL       = เห็นทุกใบในสาขา (ใช้กับ A-Admin / M-Messenger)
   การตัดสินใจว่าจะส่งค่าไหน เป็นหน้าที่ของ service layer

   "เกี่ยวข้อง" = เป็นผู้แจ้ง (RequesterEmpCode) **หรือ** เป็นคนกดสร้าง (CreatedBy) ตาม D37
   ต้องรวมคนกดสร้างด้วย ไม่งั้นใบที่แจ้งแทนคนอื่นตาม D17 จะหายไปจากรายการของคนกรอก
   ทันทีที่บันทึก และหากลับมาดูอีกไม่ได้เลย

   @SendDateFrom / @SendDateTo       : ช่วงวันส่ง (ส่ง NULL = ไม่กรอง)
   @RequestDateFrom / @RequestDateTo : ช่วง "วันที่บันทึก" (ส่ง NULL = ไม่กรอง)
   @Status                           : สถานะเดียว (ส่ง NULL = ทุกสถานะ)

   คืน result set 2 ชุด :
     ชุดที่ 1 = ใบแจ้งงานที่ผ่านเงื่อนไข
     ชุดที่ 2 = ประเภทงาน (พร้อม DetailText) ของใบงานในชุดที่ 1 ทั้งหมด

   ทำไมต้องมีชุดที่ 2 ทั้งที่ view มี JobTypeCodes อยู่แล้ว :
   JobTypeCodes รวมมาแค่ "รหัส" ประเภทงาน ไม่มี DetailText เพราะ DetailText เป็น
   ข้อความอิสระของผู้ใช้ จะเอามาต่อสตริงคั่นด้วยจุลภาคแล้วแยกกลับให้ถูกไม่ได้
   (ผู้ใช้พิมพ์จุลภาคเองก็พังทันที) จึงคืนเป็นแถวจริงอีกชุด — ยังเป็น query ชุดเดียว
   ไม่ใช่ N+1 แบบเรียก spRequestJobTypeListByReq ทีละใบ
   ============================================================= */
IF OBJECT_ID(N'dbo.spDeliveryRequestList', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spDeliveryRequestList;
GO

CREATE PROCEDURE dbo.spDeliveryRequestList
    @BranchCode         CHAR(3),
    @VisibleToEmpCode   VARCHAR(20) = NULL,
    @SendDateFrom       DATE        = NULL,
    @SendDateTo         DATE        = NULL,
    @RequestDateFrom    DATE        = NULL,
    @RequestDateTo      DATE        = NULL,
    @Status             VARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- RequestDateTime เก็บเวลาด้วย จึงเทียบขอบบนเป็น "ก่อนเที่ยงคืนของวันถัดไป"
    -- แทนการใช้ <= @RequestDateTo ซึ่งจะตัดงานที่แจ้งหลัง 00:00 ของวันนั้นทิ้งทั้งวัน
    DECLARE @RequestDateToExclusive DATETIME2(0) =
        CASE WHEN @RequestDateTo IS NULL THEN NULL ELSE DATEADD(DAY, 1, CAST(@RequestDateTo AS DATETIME2(0))) END;

    -- คัดใบงานที่ผ่านเงื่อนไขเก็บไว้ก่อน แล้วให้ result set ทั้งสองชุดอ้างอิงชุดนี้
    -- จะได้ไม่ต้องเขียน WHERE ซ้ำสองที่ (ซึ่งวันหนึ่งจะแก้ที่เดียวแล้วหลุดไม่ตรงกัน)
    DECLARE @Matched TABLE (ReqId INT NOT NULL PRIMARY KEY);

    INSERT INTO @Matched (ReqId)
    SELECT ReqId
    FROM dbo.vwDeliveryRequest
    WHERE BranchCode = @BranchCode
      AND (@VisibleToEmpCode IS NULL
           OR RequesterEmpCode = @VisibleToEmpCode
           OR CreatedBy        = @VisibleToEmpCode)
      AND (@SendDateFrom     IS NULL OR SendDate >= @SendDateFrom)
      AND (@SendDateTo       IS NULL OR SendDate <= @SendDateTo)
      AND (@RequestDateFrom  IS NULL OR RequestDateTime >= @RequestDateFrom)
      AND (@RequestDateToExclusive IS NULL OR RequestDateTime < @RequestDateToExclusive)
      AND (@Status           IS NULL OR Status = @Status);

    -- ชุดที่ 1 : ใบแจ้งงาน
    SELECT v.ReqId, v.ReqNo, v.BranchCode, v.BranchName,
           v.RequesterEmpCode, v.RequesterName, v.RequesterDeptCode, v.RequesterUnitName,
           v.RequesterPhoneExt, v.RequesterEmail,
           v.RequestDateTime, v.SendDate, v.ContactName, v.Address, v.Phone, v.Detail,
           v.Status, v.IsPersonal, v.ReceiptConfirmed, v.ReceiptConfirmedAt, v.ReceiptConfirmedBy, v.[RowVersion],
           v.CreatedBy, v.CreatedAt, v.UpdatedBy, v.UpdatedAt,
           v.MessengerEmpCode, v.MessengerName, v.ConfirmedAt, v.SequenceOrder,
           v.Route, v.DistanceKm, v.ReturnToOffice, v.JobTypeCodes
    FROM dbo.vwDeliveryRequest AS v
    INNER JOIN @Matched AS m
            ON m.ReqId = v.ReqId
    ORDER BY v.SendDate DESC, v.ReqNo DESC;

    -- ชุดที่ 2 : ประเภทงานของใบงานข้างบน (พร้อมรายละเอียดต่อประเภท)
    SELECT j.ReqJobTypeId, j.ReqId, j.JobType, j.DetailText
    FROM dbo.tblRequestJobType AS j
    INNER JOIN @Matched AS m
            ON m.ReqId = j.ReqId
    ORDER BY j.ReqId, j.ReqJobTypeId;
END
GO

PRINT '--- 050_StoredProcedures_DeliveryRequest.sql completed ---';
GO
