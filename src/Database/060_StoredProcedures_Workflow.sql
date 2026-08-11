/* =============================================================
   060_StoredProcedures_Workflow.sql
   Stored procedures ของ Phase 2 : workflow ของ Messenger
   -------------------------------------------------------------
   - รันซ้ำได้ : DROP แล้ว CREATE ใหม่
   - เข้ากับ SQL Server 2014 (compat 120) — ห้ามใช้ CREATE OR ALTER
   - ทุก procedure รับ @BranchCode และใช้เป็นเงื่อนไขเสมอ (BR-6)

   หลักการร่วมของไฟล์นี้ :
   การเปลี่ยนสถานะทุกครั้งเป็น "update แบบมีเงื่อนไขสถานะเดิม"
   (WHERE Status = @FromStatus) ไม่ใช่ update ทับ — เพื่อให้กรณีที่สองคน
   กดปุ่มพร้อมกัน มีเพียงคนแรกที่สำเร็จ ส่วนคนที่สองได้ @RowsAffected = 0
   แล้ว service layer จะแจ้งว่า "ถูกเปลี่ยนสถานะไปแล้ว"

   procedure เหล่านี้ "ไม่" ตรวจว่า transition ถูกต้องตาม §6 หรือผู้ใช้มีสิทธิ์ไหม
   นั่นเป็นหน้าที่ของ service layer ตาม §10 ข้อ 6
   ============================================================= */

USE [MessengerDb];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* =============================================================
   spDeliveryRequestChangeStatus — เปลี่ยนสถานะ 1 ครั้ง

   ทำ 3 อย่างใน transaction เดียว :
   1) เปลี่ยนสถานะ (เฉพาะเมื่อสถานะปัจจุบันยังเป็น @FromStatus)
   2) บันทึก tblStatusHistory
   3) บันทึกเหตุผลลง tblPauseReason / tblCancelReason ตามปลายทาง (ถ้ามี)

   ไม่แตะ UpdatedBy/UpdatedAt ของใบงานโดยตั้งใจ — สองคอลัมน์นั้นหมายถึง
   "แก้ไขเนื้อหาใบงานล่าสุด" (BR-2) ส่วนประวัติการเปลี่ยนสถานะอยู่ใน
   tblStatusHistory ครบอยู่แล้ว
   ============================================================= */
IF OBJECT_ID(N'dbo.spDeliveryRequestChangeStatus', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spDeliveryRequestChangeStatus;
GO

CREATE PROCEDURE dbo.spDeliveryRequestChangeStatus
    @ReqId          INT,
    @BranchCode     CHAR(3),
    @FromStatus     VARCHAR(20),
    @ToStatus       VARCHAR(20),
    @ByEmpCode      VARCHAR(20),
    @ChangedAt      DATETIME2(0),
    @Note           NVARCHAR(1000) = NULL,
    @Reason         NVARCHAR(1000) = NULL,
    @RowsAffected   INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    UPDATE dbo.tblDeliveryRequest
    SET Status = @ToStatus
    WHERE ReqId      = @ReqId
      AND BranchCode = @BranchCode
      AND Status     = @FromStatus;

    SET @RowsAffected = @@ROWCOUNT;

    IF @RowsAffected = 0
    BEGIN
        -- มีคนกดตัดหน้าไปแล้ว หรือใบงานอยู่คนละสาขา
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.tblStatusHistory (ReqId, FromStatus, ToStatus, ByEmpCode, ChangedAt, Note)
    VALUES (@ReqId, @FromStatus, @ToStatus, @ByEmpCode, @ChangedAt, @Note);

    IF @ToStatus = 'Paused' AND @Reason IS NOT NULL
    BEGIN
        INSERT INTO dbo.tblPauseReason (ReqId, Reason, ByEmpCode, PausedAt)
        VALUES (@ReqId, @Reason, @ByEmpCode, @ChangedAt);
    END

    IF @ToStatus = 'Cancelled' AND @Reason IS NOT NULL
    BEGIN
        -- Cancelled เป็น terminal จึงเกิดได้ครั้งเดียวต่อใบ (UQtblCancelReasonReqId)
        INSERT INTO dbo.tblCancelReason (ReqId, Reason, ByEmpCode, CancelledAt)
        VALUES (@ReqId, @Reason, @ByEmpCode, @ChangedAt);
    END

    COMMIT TRANSACTION;
END
GO

/* =============================================================
   spMessengerAssignmentConfirm — Messenger ยืนยันรับงาน
   (Received → Delivering พร้อมจองลำดับวิ่งงานของวันนั้น)

   D11 — SequenceOrder เป็นลำดับของ "สาขา + วันที่ส่ง" ไม่ได้แยกต่อ Messenger
         เลขถัดไป = MAX ของวันนั้น + 1

   UPDLOCK + SERIALIZABLE ตอนหาเลขถัดไป กันกรณีที่ยืนยันสองใบพร้อมกัน
   แล้วได้ลำดับซ้ำกัน (อ่านค่า MAX เดิมทั้งคู่)
   ============================================================= */
IF OBJECT_ID(N'dbo.spMessengerAssignmentConfirm', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spMessengerAssignmentConfirm;
GO

CREATE PROCEDURE dbo.spMessengerAssignmentConfirm
    @ReqId              INT,
    @BranchCode         CHAR(3),
    @MessengerEmpCode   VARCHAR(20),
    @ByEmpCode          VARCHAR(20),
    @ConfirmedAt        DATETIME2(0),
    @Note               NVARCHAR(1000) = NULL,
    @SequenceOrder      INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @SequenceOrder = NULL;

    BEGIN TRANSACTION;

    UPDATE dbo.tblDeliveryRequest
    SET Status = 'Delivering'
    WHERE ReqId      = @ReqId
      AND BranchCode = @BranchCode
      AND Status     = 'Received';

    IF @@ROWCOUNT = 0
    BEGIN
        -- ใบงานไม่ได้อยู่สถานะ Received แล้ว = มีคนยืนยันตัดหน้า หรือคนละสาขา
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @SendDate DATE;

    SELECT @SendDate = SendDate
    FROM dbo.tblDeliveryRequest
    WHERE ReqId = @ReqId;

    SELECT @SequenceOrder = ISNULL(MAX(a.SequenceOrder), 0) + 1
    FROM dbo.tblMessengerAssignment AS a WITH (UPDLOCK, SERIALIZABLE)
    INNER JOIN dbo.tblDeliveryRequest AS r
            ON r.ReqId = a.ReqId
    WHERE r.BranchCode = @BranchCode
      AND r.SendDate   = @SendDate;

    INSERT INTO dbo.tblMessengerAssignment (ReqId, MessengerEmpCode, ConfirmedAt, SequenceOrder)
    VALUES (@ReqId, @MessengerEmpCode, @ConfirmedAt, @SequenceOrder);

    INSERT INTO dbo.tblStatusHistory (ReqId, FromStatus, ToStatus, ByEmpCode, ChangedAt, Note)
    VALUES (@ReqId, 'Received', 'Delivering', @ByEmpCode, @ConfirmedAt, @Note);

    COMMIT TRANSACTION;
END
GO

/* =============================================================
   spMessengerAssignmentSwapSequence — สลับลำดับวิ่งงานของใบงาน 2 ใบ

   ใช้กับปุ่มเลื่อนขึ้น/ลงในหน้าคิวงาน service layer เป็นคนหาว่าใบไหน
   อยู่ติดกัน แล้วส่งคู่มาให้ procedure นี้สลับให้แบบ atomic

   ตรวจซ้ำที่นี่ว่าทั้งสองใบอยู่สาขาเดียวกันและ "วันส่งเดียวกัน" จริง
   เพราะลำดับมีความหมายเฉพาะภายในวันเดียวกันเท่านั้น (D11)
   ============================================================= */
IF OBJECT_ID(N'dbo.spMessengerAssignmentSwapSequence', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spMessengerAssignmentSwapSequence;
GO

CREATE PROCEDURE dbo.spMessengerAssignmentSwapSequence
    @ReqIdA         INT,
    @ReqIdB         INT,
    @BranchCode     CHAR(3),
    @RowsAffected   INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @RowsAffected = 0;

    BEGIN TRANSACTION;

    DECLARE @SeqA INT, @SeqB INT, @DateA DATE, @DateB DATE;

    SELECT @SeqA = a.SequenceOrder, @DateA = r.SendDate
    FROM dbo.tblMessengerAssignment AS a WITH (UPDLOCK)
    INNER JOIN dbo.tblDeliveryRequest AS r
            ON r.ReqId = a.ReqId
    WHERE a.ReqId      = @ReqIdA
      AND r.BranchCode = @BranchCode;

    SELECT @SeqB = a.SequenceOrder, @DateB = r.SendDate
    FROM dbo.tblMessengerAssignment AS a WITH (UPDLOCK)
    INNER JOIN dbo.tblDeliveryRequest AS r
            ON r.ReqId = a.ReqId
    WHERE a.ReqId      = @ReqIdB
      AND r.BranchCode = @BranchCode;

    IF @SeqA IS NULL OR @SeqB IS NULL OR @DateA <> @DateB
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    UPDATE dbo.tblMessengerAssignment
    SET SequenceOrder = @SeqB, UpdatedAt = SYSDATETIME()
    WHERE ReqId = @ReqIdA;

    UPDATE dbo.tblMessengerAssignment
    SET SequenceOrder = @SeqA, UpdatedAt = SYSDATETIME()
    WHERE ReqId = @ReqIdB;

    SET @RowsAffected = 2;

    COMMIT TRANSACTION;
END
GO

/* =============================================================
   spStatusHistoryListByReq — audit trail ของใบงาน 1 ใบ (§6)

   join กับ tblDeliveryRequest ด้วย @BranchCode เพื่อบังคับ BR-6 :
   ถามประวัติของใบงานสาขาอื่น จะได้ผลลัพธ์ว่างเปล่า
   ============================================================= */
IF OBJECT_ID(N'dbo.spStatusHistoryListByReq', N'P') IS NOT NULL
    DROP PROCEDURE dbo.spStatusHistoryListByReq;
GO

CREATE PROCEDURE dbo.spStatusHistoryListByReq
    @ReqId      INT,
    @BranchCode CHAR(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT h.HistoryId,
           h.ReqId,
           h.FromStatus,
           h.ToStatus,
           h.ByEmpCode,
           e.FullName AS ByName,
           h.ChangedAt,
           h.Note
    FROM dbo.tblStatusHistory AS h
    INNER JOIN dbo.tblDeliveryRequest AS r
            ON r.ReqId = h.ReqId
    LEFT JOIN dbo.tblEmployee AS e
            ON e.EmpCode = h.ByEmpCode
    WHERE h.ReqId      = @ReqId
      AND r.BranchCode = @BranchCode
    ORDER BY h.ChangedAt, h.HistoryId;
END
GO

PRINT '--- 060_StoredProcedures_Workflow.sql completed ---';
GO
