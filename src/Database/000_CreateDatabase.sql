/* =============================================================
   000_CreateDatabase.sql
   สร้าง database หลักของระบบ Messenger Document Delivery
   -------------------------------------------------------------
   - Collation  : Thai_CI_AS (ข้อมูลเป็นภาษาไทย, เก็บด้วย NVARCHAR)
   - Compat lvl : 120 = SQL Server 2014  ** production คือ SQL 2014
                  ถึงแม้เครื่อง dev เป็น SQL 2022 ก็ตั้ง compat = 120
                  เพื่อกันการเผลอใช้ T-SQL ที่ SQL 2014 ไม่รองรับ **
   - รันซ้ำได้ (idempotent)
   ============================================================= */

USE master;
GO

IF DB_ID(N'MessengerDb') IS NULL
BEGIN
    CREATE DATABASE [MessengerDb] COLLATE Thai_CI_AS;
    PRINT 'Created database [MessengerDb].';
END
ELSE
    PRINT 'Database [MessengerDb] already exists - skipped CREATE.';
GO

ALTER DATABASE [MessengerDb] SET COMPATIBILITY_LEVEL = 120;
GO

ALTER DATABASE [MessengerDb] SET RECOVERY SIMPLE;
GO

USE [MessengerDb];
GO

DECLARE @compat NVARCHAR(10);
SELECT @compat = CONVERT(NVARCHAR(10), compatibility_level) FROM sys.databases WHERE name = DB_NAME();

PRINT 'Database  : ' + DB_NAME();
PRINT 'Collation : ' + CONVERT(NVARCHAR(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'));
PRINT 'Compat    : ' + @compat;
GO
