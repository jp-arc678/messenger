# Messenger Document Delivery System

ระบบแจ้งงานรับ-ส่งเอกสารภายนอกสำหรับเจ้าหน้าที่ Messenger (2 สาขา: SDC, SBK)

> ข้อกำหนด business rule ทั้งหมดอยู่ใน [CLAUDE.md](CLAUDE.md) — อ่านไฟล์นั้นก่อนแก้โค้ด

**สถานะปัจจุบัน: Phase 1 — ฟอร์มแจ้งงาน (User)**

| เฟส | สถานะ |
|---|---|
| Phase 0 — Foundation (โครงโปรเจกต์ + DB + auth 3 roles + SSO stub) | ✅ เสร็จ |
| Phase 1 — ฟอร์มแจ้งงาน (สร้าง/แก้/ดู, BR-1, BR-2, BR-8) | ✅ เสร็จ — รอ approval |
| Phase 2 — Messenger workflow | ⬜ ยังไม่เริ่ม |

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
    sqlcmd -S localhost -E -b -f 65001 -i $f.FullName
}
```

> ต้องมี `-f 65001` เสมอ — ไฟล์ `.sql` เป็น UTF-8 แต่ `sqlcmd` จะอ่านด้วย codepage
> ของ Windows ถ้าไม่บอก ทำให้ข้อความไทยใน seed data เพี้ยน

ได้ database `MessengerDb` (collation `Thai_CI_AS`, compatibility level 120 = SQL Server 2014)
แก้ connection string ได้ที่ [src/Web/Web.config](src/Web/Web.config)

### 2. ดาวน์โหลด Bootstrap (ไม่ใช้ CDN เพราะเป็นระบบ intranet)

```powershell
pwsh -File tools\Get-Bootstrap.ps1
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

## ตรวจทั้งหมดในคำสั่งเดียว

`Verify.ps1` รัน SQL scripts → build → unit test ให้ครบตามลำดับ (รันซ้ำได้เสมอ):

```powershell
pwsh -File tools\Verify.ps1
```

ถ้าสงสัยว่าใบงานลง DB จริงไหม / stored procedure คืนอะไร ใช้:

```powershell
pwsh -File tools\Diag-Requests.ps1
```

---

## สิ่งที่ใช้งานได้ตอนนี้ (Phase 1)

เมนู **ใบแจ้งงาน** และ **แจ้งงานใหม่** บน navbar (`/Requests`)

| หน้า | ทำอะไรได้ |
|---|---|
| `/Requests` | รายการใบงาน + กรองตามช่วง **วันที่ส่ง** · Admin/Messenger เห็นทั้งสาขา ส่วน User เห็นเฉพาะใบตัวเอง (§5 + BR-6) |
| `/Requests/Create` | สร้างใบงาน — เลือกผู้แจ้งแทนคนอื่นได้เฉพาะคนในสาขาเดียวกัน (D17), ติ๊กประเภทงาน 6 แบบพร้อมช่องรายละเอียดต่ออัน (D18) |
| `/Requests/Details/{id}` | ดูใบงาน + ปุ่มแก้ไขจะโผล่เฉพาะเมื่อมีสิทธิ์จริงตาม BR-2 |
| `/Requests/Edit/{id}` | แก้ใบงาน — ล็อกตาม BR-2 และกันแก้ชนกันด้วย rowVersion (optimistic locking) |

Business rule ที่บังคับแล้วที่ **service layer**:

- **BR-1** — sendDate default: เกิน 10:00 → วันถัดไป, ตกเสาร์/อาทิตย์ → เลื่อนเป็นจันทร์
- **D16** — sendDate ที่ผู้ใช้เลือกเอง: ห้ามย้อนหลัง ห้ามเสาร์/อาทิตย์
- **BR-2** — User เจ้าของแก้ได้เฉพาะสถานะ `Received`, Admin แก้ได้ทุกสถานะ
- **BR-6** — filter ตาม branchCode ทุก query
- **BR-8** — เลขใบงาน `MSG-{BRANCH}-{YYMM}-{NNNN}` reset รายเดือนแยกตามสาขา
- **D15** — `contactName` / `address` / `detail` บังคับกรอก · **D18** — ต้องเลือกประเภทงาน ≥ 1

### ลองด้วยมือ

1. login เป็น `10002` (User สาขา SDC) → แจ้งงานใหม่ → ได้เลขใบงาน `MSG-SDC-{YYMM}-0001`
2. login เป็น `20002` (User สาขา SBK) → **ต้องไม่เห็น** ใบงานของ SDC (BR-6) และเลขจะเริ่ม `MSG-SBK-...` แยกลำดับกัน
3. บันทึกงานหลัง 10:00 → sendDate ต้องเป็นวันถัดไป (และข้ามเสาร์-อาทิตย์ไปวันจันทร์)

> ⚠️ ยังไม่ได้ทำ: คิวงาน/ยืนยันงานของ Messenger, เปลี่ยนสถานะ, รูปภาพ, อีเมล, รายงาน — อยู่ใน Phase 2–5

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
