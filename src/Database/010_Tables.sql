/* =============================================================
   010_Tables.sql
   สร้างตารางหลักทั้งหมดตาม CLAUDE.md §8 Data Model
   -------------------------------------------------------------
   - รันซ้ำได้ (idempotent) — ข้ามตารางที่มีอยู่แล้ว
   - เขียนให้เข้ากับ SQL Server 2014 (compat 120)
     ห้ามใช้ DROP ... IF EXISTS / STRING_AGG (2016+)
   - ข้อความภาษาไทยใช้ NVARCHAR ทั้งหมด
   - Naming : ตาราง = tbl, view = vw, stored procedure = sp, function = fn
              (ต่อท้ายด้วย PascalCase ไม่มี underscore คั่น)
   ============================================================= */

USE [MessengerDb];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ---------- 1) tblBranch : สาขา (หน่วย isolation ของทั้งระบบ) ---------- */
IF OBJECT_ID(N'dbo.tblBranch', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblBranch
    (
        BranchCode  CHAR(3)         NOT NULL,
        BranchName  NVARCHAR(100)   NOT NULL,
        IsActive    BIT             NOT NULL CONSTRAINT DFtblBranchIsActive  DEFAULT (1),
        CreatedAt   DATETIME2(0)    NOT NULL CONSTRAINT DFtblBranchCreatedAt DEFAULT (SYSDATETIME()),
        CONSTRAINT PKtblBranch PRIMARY KEY CLUSTERED (BranchCode)
    );
    PRINT 'Created table dbo.tblBranch';
END
GO

/* ---------- 2) tblEmployee : cache ข้อมูลพนักงานจาก SSO (BR-7) ---------- */
IF OBJECT_ID(N'dbo.tblEmployee', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblEmployee
    (
        EmpCode     VARCHAR(20)     NOT NULL,
        FullName    NVARCHAR(200)   NOT NULL,
        DeptCode    VARCHAR(20)     NULL,
        UnitName    NVARCHAR(200)   NULL,
        PhoneExt    VARCHAR(20)     NULL,
        Email       NVARCHAR(200)   NULL,
        BranchCode  CHAR(3)         NOT NULL,
        IsActive    BIT             NOT NULL CONSTRAINT DFtblEmployeeIsActive  DEFAULT (1),
        SyncedAt    DATETIME2(0)    NOT NULL CONSTRAINT DFtblEmployeeSyncedAt  DEFAULT (SYSDATETIME()),
        CreatedAt   DATETIME2(0)    NOT NULL CONSTRAINT DFtblEmployeeCreatedAt DEFAULT (SYSDATETIME()),
        UpdatedAt   DATETIME2(0)    NULL,
        CONSTRAINT PKtblEmployee            PRIMARY KEY CLUSTERED (EmpCode),
        CONSTRAINT FKtblEmployeeBranch      FOREIGN KEY (BranchCode) REFERENCES dbo.tblBranch (BranchCode)
    );
    CREATE INDEX IXtblEmployeeBranchCode ON dbo.tblEmployee (BranchCode) INCLUDE (FullName, Email);
    PRINT 'Created table dbo.tblEmployee';
END
GO

/* ---------- 3) tblUserRole : สิทธิ์ผู้ใช้ ----------
   D10 — 1 คนมีได้ 1 role เท่านั้น ห้ามซ้อน  →  PK เป็น EmpCode เดี่ยว ๆ
   คนที่ไม่มีแถวในตารางนี้ = U-User โดยปริยาย (resolve ที่ service layer)
   -------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblUserRole', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblUserRole
    (
        EmpCode     VARCHAR(20)     NOT NULL,
        BranchCode  CHAR(3)         NOT NULL,
        RoleCode    CHAR(1)         NOT NULL,   -- A=Admin, U=User, M=Messenger
        CreatedBy   VARCHAR(20)     NULL,
        CreatedAt   DATETIME2(0)    NOT NULL CONSTRAINT DFtblUserRoleCreatedAt DEFAULT (SYSDATETIME()),
        UpdatedBy   VARCHAR(20)     NULL,
        UpdatedAt   DATETIME2(0)    NULL,
        CONSTRAINT PKtblUserRole            PRIMARY KEY CLUSTERED (EmpCode),
        CONSTRAINT FKtblUserRoleEmployee    FOREIGN KEY (EmpCode)    REFERENCES dbo.tblEmployee (EmpCode),
        CONSTRAINT FKtblUserRoleBranch      FOREIGN KEY (BranchCode) REFERENCES dbo.tblBranch (BranchCode),
        CONSTRAINT CKtblUserRoleRoleCode    CHECK (RoleCode IN ('A', 'U', 'M'))
    );
    CREATE INDEX IXtblUserRoleBranchRole ON dbo.tblUserRole (BranchCode, RoleCode);
    PRINT 'Created table dbo.tblUserRole';
END
GO

/* ---------- 4) tblReqNoSequence : running number ตาม BR-8 ----------
   แยกลำดับตาม (สาขา + YYMM) และ reset ทุกเดือนโดยธรรมชาติ
   เพราะขึ้นแถวใหม่เมื่อเปลี่ยนเดือน
   ----------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblReqNoSequence', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblReqNoSequence
    (
        BranchCode  CHAR(3)         NOT NULL,
        YyMm        CHAR(4)         NOT NULL,   -- ปี 2 หลัก + เดือน 2 หลัก เช่น '2608'
        LastNumber  INT             NOT NULL CONSTRAINT DFtblReqNoSequenceLastNumber DEFAULT (0),
        UpdatedAt   DATETIME2(0)    NOT NULL CONSTRAINT DFtblReqNoSequenceUpdatedAt  DEFAULT (SYSDATETIME()),
        CONSTRAINT PKtblReqNoSequence       PRIMARY KEY CLUSTERED (BranchCode, YyMm),
        CONSTRAINT FKtblReqNoSequenceBranch FOREIGN KEY (BranchCode) REFERENCES dbo.tblBranch (BranchCode),
        CONSTRAINT CKtblReqNoSequenceLast   CHECK (LastNumber >= 0)
    );
    PRINT 'Created table dbo.tblReqNoSequence';
END
GO

/* ---------- 5) tblDeliveryRequest : ใบแจ้งงาน (entity หลัก) ----------
   D15 — ContactName / Address / Detail เป็นข้อมูลบังคับกรอก
         จึงเป็น NOT NULL และมี CHECK กันการส่งช่องว่างล้วนมาแทน
         ส่วน Phone ยังปล่อยว่างได้
   -------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblDeliveryRequest', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblDeliveryRequest
    (
        ReqId               INT             IDENTITY(1,1) NOT NULL,
        ReqNo               VARCHAR(20)     NOT NULL,   -- MSG-{BRANCH}-{YYMM}-{NNNN}
        BranchCode          CHAR(3)         NOT NULL,   -- BR-6 branch isolation
        RequesterEmpCode    VARCHAR(20)     NOT NULL,   -- D17 อาจไม่ใช่คนที่กดสร้าง (แจ้งแทนได้)
        RequestDateTime     DATETIME2(0)    NOT NULL,   -- เวลาที่บันทึก (ใช้คิด BR-1)
        SendDate            DATE            NOT NULL,   -- วันที่ส่ง (BR-1)
        ContactName         NVARCHAR(200)   NOT NULL,
        Address             NVARCHAR(1000)  NOT NULL,
        Phone               VARCHAR(50)     NULL,
        Detail              NVARCHAR(MAX)   NOT NULL,
        Status              VARCHAR(20)     NOT NULL,
        IsPersonal          BIT             NOT NULL CONSTRAINT DFtblDeliveryRequestIsPersonal DEFAULT (0),
        -- BR-4 : ถ้าใบงานมี ReceiveDoc ต้องกดยืนยันรับของก่อนปิดงาน (ไม่บังคับรูป)
        ReceiptConfirmed    BIT             NOT NULL CONSTRAINT DFtblDeliveryRequestReceiptConfirmed DEFAULT (0),
        ReceiptConfirmedAt  DATETIME2(0)    NULL,
        ReceiptConfirmedBy  VARCHAR(20)     NULL,
        [RowVersion]        ROWVERSION      NOT NULL,   -- BR-2 optimistic locking
        CreatedBy           VARCHAR(20)     NOT NULL,   -- คนที่กดสร้างจริง
        CreatedAt           DATETIME2(0)    NOT NULL CONSTRAINT DFtblDeliveryRequestCreatedAt DEFAULT (SYSDATETIME()),
        UpdatedBy           VARCHAR(20)     NULL,
        UpdatedAt           DATETIME2(0)    NULL,
        CONSTRAINT PKtblDeliveryRequest             PRIMARY KEY CLUSTERED (ReqId),
        CONSTRAINT UQtblDeliveryRequestReqNo        UNIQUE (ReqNo),
        CONSTRAINT FKtblDeliveryRequestBranch       FOREIGN KEY (BranchCode)       REFERENCES dbo.tblBranch (BranchCode),
        CONSTRAINT FKtblDeliveryRequestRequester    FOREIGN KEY (RequesterEmpCode) REFERENCES dbo.tblEmployee (EmpCode),
        CONSTRAINT CKtblDeliveryRequestStatus       CHECK (Status IN ('Received', 'Delivering', 'Paused', 'Completed', 'Cancelled')),
        CONSTRAINT CKtblDeliveryRequestContactName  CHECK (LEN(LTRIM(RTRIM(ContactName))) > 0),
        CONSTRAINT CKtblDeliveryRequestAddress      CHECK (LEN(LTRIM(RTRIM(Address))) > 0),
        CONSTRAINT CKtblDeliveryRequestDetail       CHECK (LEN(LTRIM(RTRIM(Detail))) > 0)
    );

    -- คิวงานของสาขา (Phase 2) : filter ตามสาขา + สถานะ + วันส่ง
    CREATE INDEX IXtblDeliveryRequestBranchStatusSendDate
        ON dbo.tblDeliveryRequest (BranchCode, Status, SendDate) INCLUDE (ReqNo, RequesterEmpCode);
    -- "ใบงานของฉัน" (U-User เห็นเฉพาะใบตัวเอง)
    CREATE INDEX IXtblDeliveryRequestBranchRequester
        ON dbo.tblDeliveryRequest (BranchCode, RequesterEmpCode) INCLUDE (ReqNo, Status, SendDate);
    -- ค้นตามช่วงวันบันทึก
    CREATE INDEX IXtblDeliveryRequestBranchRequestDateTime
        ON dbo.tblDeliveryRequest (BranchCode, RequestDateTime);

    PRINT 'Created table dbo.tblDeliveryRequest';
END
GO

/* ---------- 6) tblRequestJobType : ประเภทงาน (1 ใบมีได้หลายประเภท) ---------- */
IF OBJECT_ID(N'dbo.tblRequestJobType', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblRequestJobType
    (
        ReqJobTypeId    INT             IDENTITY(1,1) NOT NULL,
        ReqId           INT             NOT NULL,
        JobType         VARCHAR(20)     NOT NULL,
        DetailText      NVARCHAR(500)   NULL,
        CONSTRAINT PKtblRequestJobType          PRIMARY KEY CLUSTERED (ReqJobTypeId),
        CONSTRAINT UQtblRequestJobTypeReqType   UNIQUE (ReqId, JobType),
        CONSTRAINT FKtblRequestJobTypeRequest   FOREIGN KEY (ReqId) REFERENCES dbo.tblDeliveryRequest (ReqId) ON DELETE CASCADE,
        CONSTRAINT CKtblRequestJobTypeJobType   CHECK (JobType IN ('SendDoc', 'ReceiveDoc', 'ReceiveCheck', 'PlaceBill', 'RenewTax', 'Other'))
    );
    PRINT 'Created table dbo.tblRequestJobType';
END
GO

/* ---------- 7) tblMessengerAssignment : การรับงานของ Messenger ----------
   D11 — แต่ละสาขามี Messenger ประจำคนเดียว เปลี่ยนตัวกลางคันไม่ได้
         → 1 ใบงาน = 1 assignment (UNIQUE ReqId)
         → SequenceOrder เป็นลำดับ "ต่อวัน" ไม่แยกต่อ Messenger
   ------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblMessengerAssignment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblMessengerAssignment
    (
        AssignmentId        INT             IDENTITY(1,1) NOT NULL,
        ReqId               INT             NOT NULL,
        MessengerEmpCode    VARCHAR(20)     NOT NULL,
        ConfirmedAt         DATETIME2(0)    NOT NULL,
        SequenceOrder       INT             NOT NULL,
        Route               NVARCHAR(500)   NULL,
        DistanceKm          DECIMAL(9,2)    NULL,
        ReturnToOffice      BIT             NULL,
        CreatedAt           DATETIME2(0)    NOT NULL CONSTRAINT DFtblMessengerAssignmentCreatedAt DEFAULT (SYSDATETIME()),
        UpdatedAt           DATETIME2(0)    NULL,
        CONSTRAINT PKtblMessengerAssignment             PRIMARY KEY CLUSTERED (AssignmentId),
        CONSTRAINT UQtblMessengerAssignmentReqId        UNIQUE (ReqId),
        CONSTRAINT FKtblMessengerAssignmentRequest      FOREIGN KEY (ReqId)            REFERENCES dbo.tblDeliveryRequest (ReqId) ON DELETE CASCADE,
        CONSTRAINT FKtblMessengerAssignmentMessenger    FOREIGN KEY (MessengerEmpCode) REFERENCES dbo.tblEmployee (EmpCode),
        CONSTRAINT CKtblMessengerAssignmentSequence     CHECK (SequenceOrder > 0)
    );
    CREATE INDEX IXtblMessengerAssignmentMessenger ON dbo.tblMessengerAssignment (MessengerEmpCode, ConfirmedAt);
    PRINT 'Created table dbo.tblMessengerAssignment';
END
GO

/* ---------- 8) tblDeliveryPhoto : รูปยืนยัน (BR-3, เก็บ path เท่านั้น) ---------- */
IF OBJECT_ID(N'dbo.tblDeliveryPhoto', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblDeliveryPhoto
    (
        PhotoId         INT             IDENTITY(1,1) NOT NULL,
        ReqId           INT             NOT NULL,
        PhotoType       VARCHAR(10)     NOT NULL,   -- send / receive
        FilePath        NVARCHAR(500)   NOT NULL,   -- path บน filesystem (ไม่เก็บ binary)
        FileName        NVARCHAR(255)   NULL,
        FileSizeBytes   INT             NULL,
        CapturedAt      DATETIME2(0)    NOT NULL,
        CapturedBy      VARCHAR(20)     NOT NULL,
        CONSTRAINT PKtblDeliveryPhoto           PRIMARY KEY CLUSTERED (PhotoId),
        CONSTRAINT FKtblDeliveryPhotoRequest    FOREIGN KEY (ReqId) REFERENCES dbo.tblDeliveryRequest (ReqId) ON DELETE CASCADE,
        CONSTRAINT CKtblDeliveryPhotoPhotoType  CHECK (PhotoType IN ('send', 'receive'))
    );
    CREATE INDEX IXtblDeliveryPhotoReqType ON dbo.tblDeliveryPhoto (ReqId, PhotoType);
    PRINT 'Created table dbo.tblDeliveryPhoto';
END
GO

/* ---------- 9) tblStatusHistory : audit trail ทุกการเปลี่ยนสถานะ (§6) ----------
   FromStatus = NULL หมายถึงตอนสร้างใบงานใหม่
   ---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblStatusHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblStatusHistory
    (
        HistoryId   BIGINT          IDENTITY(1,1) NOT NULL,
        ReqId       INT             NOT NULL,
        FromStatus  VARCHAR(20)     NULL,
        ToStatus    VARCHAR(20)     NOT NULL,
        ByEmpCode   VARCHAR(20)     NOT NULL,
        ChangedAt   DATETIME2(0)    NOT NULL CONSTRAINT DFtblStatusHistoryChangedAt DEFAULT (SYSDATETIME()),
        Note        NVARCHAR(1000)  NULL,
        CONSTRAINT PKtblStatusHistory           PRIMARY KEY CLUSTERED (HistoryId),
        CONSTRAINT FKtblStatusHistoryRequest    FOREIGN KEY (ReqId) REFERENCES dbo.tblDeliveryRequest (ReqId) ON DELETE CASCADE,
        CONSTRAINT CKtblStatusHistoryFromStatus CHECK (FromStatus IS NULL OR FromStatus IN ('Received', 'Delivering', 'Paused', 'Completed', 'Cancelled')),
        CONSTRAINT CKtblStatusHistoryToStatus   CHECK (ToStatus IN ('Received', 'Delivering', 'Paused', 'Completed', 'Cancelled'))
    );
    CREATE INDEX IXtblStatusHistoryReq ON dbo.tblStatusHistory (ReqId, ChangedAt);
    PRINT 'Created table dbo.tblStatusHistory';
END
GO

/* ---------- 10) tblPauseReason : เหตุผลการพักงาน (พักได้หลายครั้ง) ---------- */
IF OBJECT_ID(N'dbo.tblPauseReason', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPauseReason
    (
        PauseReasonId   INT             IDENTITY(1,1) NOT NULL,
        ReqId           INT             NOT NULL,
        Reason          NVARCHAR(1000)  NOT NULL,
        ByEmpCode       VARCHAR(20)     NOT NULL,
        PausedAt        DATETIME2(0)    NOT NULL CONSTRAINT DFtblPauseReasonPausedAt DEFAULT (SYSDATETIME()),
        CONSTRAINT PKtblPauseReason         PRIMARY KEY CLUSTERED (PauseReasonId),
        CONSTRAINT FKtblPauseReasonRequest  FOREIGN KEY (ReqId) REFERENCES dbo.tblDeliveryRequest (ReqId) ON DELETE CASCADE
    );
    CREATE INDEX IXtblPauseReasonReq ON dbo.tblPauseReason (ReqId, PausedAt);
    PRINT 'Created table dbo.tblPauseReason';
END
GO

/* ---------- 11) tblCancelReason : เหตุผลการยกเลิก (terminal → 1 ใบ 1 ครั้ง) ---------- */
IF OBJECT_ID(N'dbo.tblCancelReason', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblCancelReason
    (
        CancelReasonId  INT             IDENTITY(1,1) NOT NULL,
        ReqId           INT             NOT NULL,
        Reason          NVARCHAR(1000)  NOT NULL,
        ByEmpCode       VARCHAR(20)     NOT NULL,
        CancelledAt     DATETIME2(0)    NOT NULL CONSTRAINT DFtblCancelReasonCancelledAt DEFAULT (SYSDATETIME()),
        CONSTRAINT PKtblCancelReason        PRIMARY KEY CLUSTERED (CancelReasonId),
        CONSTRAINT UQtblCancelReasonReqId   UNIQUE (ReqId),
        CONSTRAINT FKtblCancelReasonRequest FOREIGN KEY (ReqId) REFERENCES dbo.tblDeliveryRequest (ReqId) ON DELETE CASCADE
    );
    PRINT 'Created table dbo.tblCancelReason';
END
GO

PRINT '--- 010_Tables.sql completed ---';
GO
