# Messenger Document Delivery System

ระบบแจ้งงานรับ-ส่งเอกสารภายนอกสำหรับเจ้าหน้าที่ Messenger (2 สาขา: SDC, SBK)

> ข้อกำหนด business rule ทั้งหมดอยู่ใน [CLAUDE.md](CLAUDE.md) — อ่านไฟล์นั้นก่อนแก้โค้ด

**สถานะปัจจุบัน: Phase 0 — Foundation**

---

## โครงสร้าง

```
/src
  /Domain          enum + entity (ไม่ขึ้นกับใคร)
  /Application     interface + DTO + service (business rule ทั้งหมดอยู่ที่นี่)
  /Infrastructure  Dapper → stored procedure, SSO client, connection factory
  /Web             ASP.NET MVC 5 (Razor + Bootstrap 5)
  /Database        DDL / view / stored procedure / seed — เรียงตามลำดับการรัน
/tests
  /UnitTests       NUnit
/tools             สคริปต์ช่วยงาน
```

---

## เตรียมเครื่องครั้งแรก

**ต้องมี:** VS 2022 Build Tools (workload `.NET desktop` + `Web development` + .NET Framework 4.8), SQL Server, .NET SDK (ใช้เฉพาะ `dotnet test`)

### 1. สร้าง database

รันตามลำดับเลขไฟล์ ทุกสคริปต์รันซ้ำได้:

```powershell
foreach ($f in Get-ChildItem src\Database\*.sql | Sort-Object Name) {
    sqlcmd -S localhost -E -b -i $f.FullName
}
```

ได้ database `MessengerDb` (collation `Thai_CI_AS`, compatibility level 120 = SQL Server 2014)
แก้ connection string ได้ที่ [src/Web/Web.config](src/Web/Web.config)

### 2. Bootstrap

ไฟล์ Bootstrap 5.3.3 ถูก commit ไว้ในรีโปแล้ว (ไม่ใช้ CDN เพราะเป็นระบบ intranet) ไม่ต้องทำอะไรเพิ่ม

ถ้าจะอัปเกรดเวอร์ชัน:

```powershell
pwsh -File tools\Get-Bootstrap.ps1 -Version 5.3.4
```

---

## Build

```powershell
$msb = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
& $msb Messenger.sln -t:restore,build
```

## รันเว็บ

```powershell
& "C:\Program Files\IIS Express\iisexpress.exe" /path:"$PWD\src\Web" /port:52080
```

เปิด <http://localhost:52080/> → จะถูก redirect ไปหน้า login

## รันเทสต์

```powershell
dotnet test tests\UnitTests\Messenger.UnitTests.csproj
```

---

## รหัสพนักงานสำหรับทดสอบ (SSO จำลอง)

| รหัส | ชื่อ | สาขา | สิทธิ์ |
|---|---|---|---|
| 10001 | สมชาย ใจดี | SDC | Admin |
| 10002 | สมหญิง รักงาน | SDC | User |
| 10003 | ประเสริฐ ว่องไว | SDC | Messenger |
| 10004 | อารีย์ พากเพียร | SDC | User |
| 20001 | วิชัย มั่นคง | SBK | Admin |
| 20002 | นภา สดใส | SBK | User |
| 20003 | ธนา เร็วรี่ | SBK | Messenger |
| 20004 | ชูใจ ตั้งใจ | SBK | User |
| 10099 / 20099 | พนักงานใหม่ | SDC / SBK | *(ยังไม่มีใน DB — ใช้ทดสอบ BR-7 + D10)* |

SSO ยังเป็น stub ([MockSsoClient](src/Infrastructure/Sso/MockSsoClient.cs)) ตาม D3
เมื่อได้ contract จริงให้เขียน `ISsoClient` ตัวใหม่ แล้วสลับบรรทัดเดียวใน
[ServiceRegistry](src/Web/Composition/ServiceRegistry.cs)
