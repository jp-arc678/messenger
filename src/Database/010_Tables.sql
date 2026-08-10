/* =============================================================
   010_Tables.sql
   สร้างตารางหลักทั้งหมดตาม CLAUDE.md §8 Data Model
   -------------------------------------------------------------
   - รันซ้ำได้ (idempotent) — ข้ามตารางที่มีอยู่แล้ว
   - เขียนให้เข้ากับ SQL Server 2014 (compat 120)
     ห้ามใช้ DROP ... IF EXISTS / STRING_AGG (2016+)
   - ข้อความภาษาไทยใช้ NVARCHAR ทั้งหมด
   ============================================================= */

USE [MessengerDb];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ---------- 1) Branch : สาขา (หน่วย isolation ของทั้งระบบ) ---------- */
IF OBJECT_ID(N'dbo.Branch', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Branch
    (
        BranchCode  CHAR(3)         NOT NULL,
        BranchName  NVARCHAR(100)   NOT NULL,
        IsActive    BIT             NOT NULL CONSTRAINT DF_Branch_IsActive  DEFAULT (1),
        CreatedAt   DATETIME2(0)    NOT NULL CONSTRAINT DF_Branch_CreatedAt DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_Branch PRIMARY KEY CLUSTERED (BranchCode)
    );
    PRINT 'Created table dbo.Branch';
END
GO

/* ---------- 2) Employee : cache ข้อมูลพนักงานจาก SSO (BR-7) ---------- */
IF OBJECT_ID(N'dbo.Employee', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Employee
    (
        EmpCode     VARCHAR(20)     NOT NULL,
        FullName    NVARCHAR(200)   NOT NULL,
        DeptCode    VARCHAR(20)     NULL,
        UnitName    NVARCHAR(200)   NULL,
        PhoneExt    VARCHAR(20)     NULL,
        Email       NVARCHAR(200)   NULL,
        BranchCode  CHAR(3)         NOT NULL,
        IsActive    BIT             NOT NULL CONSTRAINT DF_Employee_IsActive  DEFAULT (1),
        SyncedAt    DATETIME2(0)    NOT NULL CONSTRAINT DF_Employee_SyncedAt  DEFAULT (SYSDATETIME()),
        CreatedAt   DATETIME2(0)    NOT NULL CONSTRAINT DF_Employee_CreatedAt DEFAULT (SYSDATETIME()),
        UpdatedAt   DATETIME2(0)    NULL,
        CONSTRAINT PK_Employee          PRIMARY KEY CLUSTERED (EmpCode),
        CONSTRAINT FK_Employee_Branch   FOREIGN KEY (BranchCode) REFERENCES dbo.Branch (BranchCode)
    );
    CREATE INDEX IX_Employee_BranchCode ON dbo.Employee (BranchCode) INCLUDE (FullName, Email);
    PRINT 'Created table dbo.Employee';
END
GO

/* ---------- 3) UserRole : สิทธิ์ผู้ใช้ ----------
   D10 — 1 คนมีได้ 1 role เท่านั้น ห้ามซ้อน  →  PK เป็น EmpCode เดี่ยว ๆ
   คนที่ไม่มีแถวในตารางนี้ = U-User โดยปริยาย (resolve ที่ service layer)
   ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.UserRole', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserRole
    (
        EmpCode     VARCHAR(20)     NOT NULL,
        BranchCode  CHAR(3)         NOT NULL,
        RoleCode    CHAR(1)         NOT NULL,   -- A=Admin, U=User, M=Messenger
        CreatedBy   VARCHAR(20)     NULL,
        CreatedAt   DATETIME2(0)    NOT NULL CONSTRAINT DF_UserRole_CreatedAt DEFAULT (SYSDATETIME()),
        UpdatedBy   VARCHAR(20)     NULL,
        UpdatedAt   DATETIME2(0)    NULL,
        CONSTRAINT PK_UserRole           PRIMARY KEY CLUSTERED (EmpCode),
        CONSTRAINT FK_UserRole_Employee  FOREIGN KEY (EmpCode)    REFERENCES dbo.Employee (EmpCode),
        CONSTRAINT FK_UserRole_Branch    FOREIGN KEY (BranchCode) REFERENCES dbo.Branch (BranchCode),
        CONSTRAINT CK_UserRole_RoleCode  CHECK (RoleCode IN ('A', 'U', 'M'))
    );
    CREATE INDEX IX_UserRole_Branch_Role ON dbo.UserRole (BranchCode, RoleCode);
    PRINT 'Created table dbo.UserRole';
END
GO

/* ---------- 4) ReqNoSequence : running number ตาม BR-8 ----------
   แยกลำดับตาม (สาขา + YYMM) และ reset ทุกเดือนโดยธรรมชาติ
   เพราะขึ้นแถวใหม่เมื่อเปลี่ยนเดือน
   --------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.ReqNoSequence', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReqNoSequence
    (
        BranchCode  CHAR(3)         NOT NULL,
        YyMm        CHAR(4)         NOT NULL,   -- ปี 2 หลัก + เดือน 2 หลัก เช่น '2608'
        LastNumber  INT             NOT NULL CONSTRAINT DF_ReqNoSequence_LastNumber DEFAULT (0),
        UpdatedAt   DATETIME2(0)    NOT NULL CONSTRAINT DF_ReqNoSequence_UpdatedAt  DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_ReqNoSequence         PRIMARY KEY CLUSTERED (BranchCode, YyMm),
        CONSTRAINT FK_ReqNoSequence_Branch  FOREIGN KEY (BranchCode) REFERENCES dbo.Branch (BranchCode),
        CONSTRAINT CK_ReqNoSequence_Last    CHECK (LastNumber >= 0)
    );
    PRINT 'Created table dbo.ReqNoSequence';
END
GO

/* ---------- 5) DeliveryRequest : ใบแจ้งงาน (entity หลัก) ---------- */
IF OBJECT_ID(N'dbo.DeliveryRequest', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DeliveryRequest
    (
        ReqId               INT             IDENTITY(1,1) NOT NULL,
        ReqNo               VARCHAR(20)     NOT NULL,   -- MSG-{BRANCH}-{YYMM}-{NNNN}
        BranchCode          CHAR(3)         NOT NULL,   -- BR-6 branch isolation
        RequesterEmpCode    VARCHAR(20)     NOT NULL,
        RequestDateTime     DATETIME2(0)    NOT NULL,   -- เวลาที่บันทึก (ใช้คิด BR-1)
        SendDate            DATE            NOT NULL,   -- วันที่ส่ง (BR-1)
        ContactName         NVARCHAR(200)   NULL,
        Address             NVARCHAR(1000)  NULL,
        Phone               VARCHAR(50)     NULL,
        Detail              NVARCHAR(MAX)   NULL,
        Status              VARCHAR(20)     NOT NULL,
        IsPersonal          BIT             NOT NULL CONSTRAINT DF_DeliveryRequest_IsPersonal DEFAULT (0),
        -- BR-4 : ถ้าใบงานมี ReceiveDoc ต้องกดยืนยันรับของก่อนปิดงาน (ไม่บังคับรูป)
        ReceiptConfirmed    BIT             NOT NULL CONSTRAINT DF_DeliveryRequest_ReceiptConfirmed DEFAULT (0),
        ReceiptConfirmedAt  DATETIME2(0)    NULL,
        ReceiptConfirmedBy  VARCHAR(20)     NULL,
        [RowVersion]        ROWVERSION      NOT NULL,   -- BR-2 optimistic locking
        CreatedBy           VARCHAR(20)     NOT NULL,
        CreatedAt           DATETIME2(0)    NOT NULL CONSTRAINT DF_DeliveryRequest_CreatedAt DEFAULT (SYSDATETIME()),
        UpdatedBy           VARCHAR(20)     NULL,
        UpdatedAt           DATETIME2(0)    NULL,
        CONSTRAINT PK_DeliveryRequest           PRIMARY KEY CLUSTERED (ReqId),
        CONSTRAINT UQ_DeliveryRequest_ReqNo     UNIQUE (ReqNo),
        CONSTRAINT FK_DeliveryRequest_Branch    FOREIGN KEY (BranchCode)       REFERENCES dbo.Branch (BranchCode),
        CONSTRAINT FK_DeliveryRequest_Requester FOREIGN KEY (RequesterEmpCode) REFERENCES dbo.Employee (EmpCode),
        CONSTRAINT CK_DeliveryRequest_Status    CHECK (Status IN ('Received', 'Delivering', 'Paused', 'Completed', 'Cancelled'))
    );

    -- คิวงานของสาขา (Phase 2) : filter ตามสาขา + สถานะ + วันส่ง
    CREATE INDEX IX_DeliveryRequest_Branch_Status_SendDate
        ON dbo.DeliveryRequest (BranchCode, Status, SendDate) INCLUDE (ReqNo, RequesterEmpCode);
    -- "ใบงานของฉัน" (U-User เห็นเฉพาะใบตัวเอง)
    CREATE INDEX IX_DeliveryRequest_Branch_Requester
        ON dbo.DeliveryRequest (BranchCode, RequesterEmpCode) INCLUDE (ReqNo, Status, SendDate);
    -- ค้นตามช่วงวันบันทึก
    CREATE INDEX IX_DeliveryRequest_Branch_RequestDateTime
        ON dbo.DeliveryRequest (BranchCode, RequestDateTime);

    PRINT 'Created table dbo.DeliveryRequest';
END
GO

/* ---------- 6) RequestJobType : ประเภทงาน (1 ใบมีได้หลายประเภท) ---------- */
IF OBJECT_ID(N'dbo.RequestJobType', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RequestJobType
    (
        ReqJobTypeId    INT             IDENTITY(1,1) NOT NULL,
        ReqId           INT             NOT NULL,
        JobType         VARCHAR(20)     NOT NULL,
        DetailText      NVARCHAR(500)   NULL,
        CONSTRAINT PK_RequestJobType            PRIMARY KEY CLUSTERED (ReqJobTypeId),
        CONSTRAINT UQ_RequestJobType_Req_Type   UNIQUE (ReqId, JobType),
        CONSTRAINT FK_RequestJobType_Request    FOREIGN KEY (ReqId) REFERENCES dbo.DeliveryRequest (ReqId) ON DELETE CASCADE,
        CONSTRAINT CK_RequestJobType_JobType    CHECK (JobType IN ('SendDoc', 'ReceiveDoc', 'ReceiveCheck', 'PlaceBill', 'RenewTax', 'Other'))
    );
    PRINT 'Created table dbo.RequestJobType';
END
GO

/* ---------- 7) MessengerAssignment : การรับงานของ Messenger ----------
   D11 — แต่ละสาขามี Messenger ประจำคนเดียว เปลี่ยนตัวกลางคันไม่ได้
         → 1 ใบงาน = 1 assignment (UNIQUE ReqId)
         → SequenceOrder เป็นลำดับ "ต่อวัน" ไม่แยกต่อ Messenger
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.MessengerAssignment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MessengerAssignment
    (
        AssignmentId        INT             IDENTITY(1,1) NOT NULL,
        ReqId               INT             NOT NULL,
        MessengerEmpCode    VARCHAR(20)     NOT NULL,
        ConfirmedAt         DATETIME2(0)    NOT NULL,
        SequenceOrder       INT             NOT NULL,
        Route               NVARCHAR(500)   NULL,
        DistanceKm          DECIMAL(9,2)    NULL,
        ReturnToOffice      BIT             NULL,
        CreatedAt           DATETIME2(0)    NOT NULL CONSTRAINT DF_MessengerAssignment_CreatedAt DEFAULT (SYSDATETIME()),
        UpdatedAt           DATETIME2(0)    NULL,
        CONSTRAINT PK_MessengerAssignment           PRIMARY KEY CLUSTERED (AssignmentId),
        CONSTRAINT UQ_MessengerAssignment_ReqId     UNIQUE (ReqId),
        CONSTRAINT FK_MessengerAssignment_Request   FOREIGN KEY (ReqId)            REFERENCES dbo.DeliveryRequest (ReqId) ON DELETE CASCADE,
        CONSTRAINT FK_MessengerAssignment_Messenger FOREIGN KEY (MessengerEmpCode) REFERENCES dbo.Employee (EmpCode),
        CONSTRAINT CK_MessengerAssignment_Sequence  CHECK (SequenceOrder > 0)
    );
    CREATE INDEX IX_MessengerAssignment_Messenger ON dbo.MessengerAssignment (MessengerEmpCode, ConfirmedAt);
    PRINT 'Created table dbo.MessengerAssignment';
END
GO

/* ---------- 8) DeliveryPhoto : รูปยืนยัน (BR-3, เก็บ path เท่านั้น) ---------- */
IF OBJECT_ID(N'dbo.DeliveryPhoto', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DeliveryPhoto
    (
        PhotoId         INT             IDENTITY(1,1) NOT NULL,
        ReqId           INT             NOT NULL,
        PhotoType       VARCHAR(10)     NOT NULL,   -- send / receive
        FilePath        NVARCHAR(500)   NOT NULL,   -- path บน filesystem (ไม่เก็บ binary)
        FileName        NVARCHAR(255)   NULL,
        FileSizeBytes   INT             NULL,
        CapturedAt      DATETIME2(0)    NOT NULL,
        CapturedBy      VARCHAR(20)     NOT NULL,
        CONSTRAINT PK_DeliveryPhoto             PRIMARY KEY CLUSTERED (PhotoId),
        CONSTRAINT FK_DeliveryPhoto_Request     FOREIGN KEY (ReqId) REFERENCES dbo.DeliveryRequest (ReqId) ON DELETE CASCADE,
        CONSTRAINT CK_DeliveryPhoto_PhotoType   CHECK (PhotoType IN ('send', 'receive'))
    );
    CREATE INDEX IX_DeliveryPhoto_Req_Type ON dbo.DeliveryPhoto (ReqId, PhotoType);
    PRINT 'Created table dbo.DeliveryPhoto';
END
GO

/* ---------- 9) StatusHistory : audit trail ทุกการเปลี่ยนสถานะ (§6) ----------
   FromStatus = NULL หมายถึงตอนสร้างใบงานใหม่
   ------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.StatusHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StatusHistory
    (
        HistoryId   BIGINT          IDENTITY(1,1) NOT NULL,
        ReqId       INT             NOT NULL,
        FromStatus  VARCHAR(20)     NULL,
        ToStatus    VARCHAR(20)     NOT NULL,
        ByEmpCode   VARCHAR(20)     NOT NULL,
        ChangedAt   DATETIME2(0)    NOT NULL CONSTRAINT DF_StatusHistory_ChangedAt DEFAULT (SYSDATETIME()),
        Note        NVARCHAR(1000)  NULL,
        CONSTRAINT PK_StatusHistory             PRIMARY KEY CLUSTERED (HistoryId),
        CONSTRAINT FK_StatusHistory_Request     FOREIGN KEY (ReqId) REFERENCES dbo.DeliveryRequest (ReqId) ON DELETE CASCADE,
        CONSTRAINT CK_StatusHistory_FromStatus  CHECK (FromStatus IS NULL OR FromStatus IN ('Received', 'Delivering', 'Paused', 'Completed', 'Cancelled')),
        CONSTRAINT CK_StatusHistory_ToStatus    CHECK (ToStatus IN ('Received', 'Delivering', 'Paused', 'Completed', 'Cancelled'))
    );
    CREATE INDEX IX_StatusHistory_Req ON dbo.StatusHistory (ReqId, ChangedAt);
    PRINT 'Created table dbo.StatusHistory';
END
GO

/* ---------- 10) PauseReason : เหตุผลการพักงาน (พักได้หลายครั้ง) ---------- */
IF OBJECT_ID(N'dbo.PauseReason', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PauseReason
    (
        PauseReasonId   INT             IDENTITY(1,1) NOT NULL,
        ReqId           INT             NOT NULL,
        Reason          NVARCHAR(1000)  NOT NULL,
        ByEmpCode       VARCHAR(20)     NOT NULL,
        PausedAt        DATETIME2(0)    NOT NULL CONSTRAINT DF_PauseReason_PausedAt DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_PauseReason           PRIMARY KEY CLUSTERED (PauseReasonId),
        CONSTRAINT FK_PauseReason_Request   FOREIGN KEY (ReqId) REFERENCES dbo.DeliveryRequest (ReqId) ON DELETE CASCADE
    );
    CREATE INDEX IX_PauseReason_Req ON dbo.PauseReason (ReqId, PausedAt);
    PRINT 'Created table dbo.PauseReason';
END
GO

/* ---------- 11) CancelReason : เหตุผลการยกเลิก (terminal → 1 ใบ 1 ครั้ง) ---------- */
IF OBJECT_ID(N'dbo.CancelReason', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CancelReason
    (
        CancelReasonId  INT             IDENTITY(1,1) NOT NULL,
        ReqId           INT             NOT NULL,
        Reason          NVARCHAR(1000)  NOT NULL,
        ByEmpCode       VARCHAR(20)     NOT NULL,
        CancelledAt     DATETIME2(0)    NOT NULL CONSTRAINT DF_CancelReason_CancelledAt DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_CancelReason          PRIMARY KEY CLUSTERED (CancelReasonId),
        CONSTRAINT UQ_CancelReason_ReqId    UNIQUE (ReqId),
        CONSTRAINT FK_CancelReason_Request  FOREIGN KEY (ReqId) REFERENCES dbo.DeliveryRequest (ReqId) ON DELETE CASCADE
    );
    PRINT 'Created table dbo.CancelReason';
END
GO

PRINT '--- 010_Tables.sql completed ---';
GO
