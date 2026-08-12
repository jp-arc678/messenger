# Messenger Document Delivery System

ระบบแจ้งงานรับ-ส่งเอกสารภายนอกสำหรับเจ้าหน้าที่ Messenger (2 สาขา: SDC, SBK)

> ข้อกำหนด business rule ทั้งหมดอยู่ใน [CLAUDE.md](CLAUDE.md) — อ่านไฟล์นั้นก่อนแก้โค้ด

**สถานะปัจจุบัน: Phase 3 — Photo + BR-4**

| เฟส | สถานะ |
|---|---|
| Phase 0 — Foundation (โครงโปรเจกต์ + DB + auth 3 roles + SSO stub) | ✅ เสร็จ |
| Phase 1 — ฟอร์มแจ้งงาน (สร้าง/แก้/ดู, BR-1, BR-2, BR-8) | ✅ เสร็จ |
| Phase 2 — Messenger workflow (คิวงาน, ยืนยัน+จัดลำดับ, state machine §6) | ✅ เสร็จ |
| Phase 3 — Photo (รูปยืนยัน BR-3 + เงื่อนไขปิดงาน BR-4) | ✅ เสร็จ — รอ approval |
| Phase 4 — Notification (อีเมลตอนปิดงาน) | ⬜ ยังไม่เริ่ม |

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

---

## Phase 2 — Messenger workflow

เมนู **คิวงานประจำวัน** (`/Queue`) จะโผล่เฉพาะ Messenger/Admin

| หน้า | ทำอะไรได้ |
|---|---|
| `/Queue?date=YYYY-MM-DD` | คิวงานของสาขาในวันนั้น แยก 3 กลุ่ม: **รอยืนยัน** → **คิววิ่งงาน** (เรียงตามลำดับ) → **ปิดแล้ว** · เลื่อนลำดับขึ้น/ลงได้ |
| `/Requests` | เพิ่มการค้นตาม **ช่วงวันที่บันทึก** และ **สถานะ** (เดิมมีแค่ช่วงวันที่ส่ง) |
| `/Requests/Details/{id}` | ปุ่มเปลี่ยนสถานะเท่าที่ role นั้นกดได้จริง + **ประวัติสถานะ** (ใคร/เมื่อไหร่/เหตุผล) |

### State machine (§6) — เส้นทางเดียวที่ระบบยอมให้เดิน

| จาก | ไป | ใครทำได้ | เหตุผล |
|---|---|---|:---:|
| Received | Delivering | Messenger / Admin | – |
| Received | Cancelled | เจ้าของใบ / Messenger / Admin | – |
| Delivering | Paused | Messenger / Admin | **บังคับ** |
| Delivering | Completed | Messenger / Admin | – |
| Delivering | Cancelled | Messenger / Admin | **บังคับ** |
| Paused | Delivering | Messenger / Admin | – |
| Paused | Cancelled | Messenger / Admin | **บังคับ** |

ตารางนี้อยู่ในโค้ดที่เดียวคือ [RequestStateMachine](src/Domain/Workflow/RequestStateMachine.cs)
และ [RequestStateMachineTests](tests/UnitTests/RequestStateMachineTests.cs) ไล่ตรวจครบทั้ง 25 คู่สถานะ
ว่าคู่ที่ไม่มีในตารางต้องเดินไม่ได้

จุดสำคัญอื่น:

- **ยืนยันรับงาน** จองลำดับวิ่งงานของ (สาขา + วันที่ส่ง) ให้อัตโนมัติ เริ่มที่ 1 ทุกวัน (D11 + D21)
- **Admin กดยืนยันแทนได้** แต่ผู้รับงานที่บันทึกคือ Messenger ประจำสาขาเสมอ (D22)
- ทุกการเปลี่ยนสถานะเขียน `tblStatusHistory` (+ `tblPauseReason` / `tblCancelReason`) ใน transaction เดียวกัน
- สองคนกดพร้อมกัน → มีคนเดียวที่สำเร็จ (update แบบมีเงื่อนไขสถานะเดิม) อีกคนได้ข้อความให้โหลดหน้าใหม่

### ลองด้วยมือ

1. login `10002` (User SDC) แจ้งงาน 2–3 ใบ → login `10003` (Messenger SDC) เปิด **คิวงานประจำวัน**
2. กด **ยืนยันรับงาน** ทีละใบ → ลำดับต้องเป็น 1, 2, 3 → กดลูกศร ↑ ↓ เพื่อสลับลำดับ
3. กด **พักการส่ง** โดยไม่กรอกเหตุผล → ต้องไม่ผ่าน · กรอกแล้วผ่าน และเหตุผลไปโผล่ในประวัติสถานะ
4. ใบที่พักอยู่ กด **ปิดงาน** ไม่ได้ (ต้อง "กลับมาส่งต่อ" ก่อน) — §6 ไม่มีเส้นทาง Paused → Completed
5. login `10004` (User SDC ที่ไม่ใช่เจ้าของใบ) → เข้า `/Queue` ไม่ได้ และไม่มีปุ่มยกเลิกบนใบของคนอื่น

---

## Phase 3 — รูปยืนยัน + เงื่อนไขปิดงาน

### รูปยืนยัน (BR-3)

ส่วน **รูปยืนยัน** อยู่ท้ายหน้ารายละเอียดใบแจ้งงาน

- อัปโหลด/ลบได้เฉพาะ **Messenger/Admin** และเฉพาะตอนใบงานสถานะ **กำลังส่ง / พักการส่ง** (D23, D24)
  ผู้แจ้งดูรูปใบตัวเองได้ แต่แตะไม่ได้
- เลือกประเภทรูป **รูปตอนส่ง / รูปตอนรับ** ต่อรูป
- ช่องเลือกไฟล์ใช้ `capture="environment"` → บนมือถือกดแล้วเปิดกล้องหลังได้เลย
- **ย่อรูปฝั่ง client ให้ ≤ 2 MB ก่อนส่ง** ([photo-upload.js](src/Web/Scripts/photo-upload.js)) —
  ย่อด้านยาวสุดเหลือ 1600px แล้วไล่ลดคุณภาพทีละขั้นจนเข้าเกณฑ์ · เบราว์เซอร์ที่ไม่รองรับจะส่งไฟล์ตรง
  แล้วให้ server ตอบว่าไฟล์ใหญ่เกิน — ไม่พังเงียบ
- ฝั่ง server ตรวจซ้ำเสมอ: ขนาด ≤ 2 MB · เป็น JPG/PNG จริงโดยดู **magic bytes** ไม่ใช่นามสกุล ·
  ไม่เกิน 20 รูปต่อใบ

**ที่เก็บไฟล์ (D25):** ตั้งที่ `PhotoStorageRoot` ใน [Web.config](src/Web/Web.config)
ค่าว่าง = `src\Web\App_Data\Photos` (dev) · production ให้ชี้ path นอกโฟลเดอร์เว็บ เช่น `D:\MessengerPhotos`
DB เก็บ path แบบสัมพัทธ์ และรูปเปิดได้ผ่าน `/Photos/Show/{id}` ที่ตรวจสาขา+สิทธิ์ก่อนเท่านั้น (BR-6)

### เงื่อนไขปิดงาน (BR-4)

ใบที่มีประเภทงาน **รับเอกสาร** (`ReceiveDoc`) ต้องกดปุ่ม **ยืนยันรับของแล้ว** ก่อน มิฉะนั้นปิดงานไม่ได้
**ไม่บังคับว่าต้องมีรูป** (D9 — รูปเป็น optional เสมอ) · ปุ่มจะโผล่เองบนหน้ารายละเอียดและในคิวงาน
เมื่อใบงานติดเงื่อนไขนี้ · การ *ยกเลิก* ไม่ติดเงื่อนไข BR-4

### ลองด้วยมือ

1. แจ้งงานโดยติ๊ก **รับเอกสาร** → login เป็น Messenger (`10003`) → ยืนยันรับงาน
2. กด **ปิดงาน** ทันที → ต้องขึ้นข้อความว่าต้องยืนยันรับของก่อน (BR-4)
3. อัปโหลดรูปจากมือถือ/ไฟล์ใหญ่ ๆ → เปิด DevTools → Network จะเห็นว่าไฟล์ที่ส่งจริงเล็กกว่า 2 MB
4. กด **ยืนยันรับของแล้ว** → ปิดงานได้ · หลังปิดงานปุ่มอัปโหลด/ลบรูปจะหายไป (D23/D24)
5. login เป็น Messenger สาขาอื่น (`20003`) แล้วเปิด `/Photos/Show/{id}` ตรง ๆ → ต้องได้ 404

> ⚠️ ยังไม่ได้ทำ: อีเมลแจ้งผู้แจ้งตอนปิดงาน (Phase 4), รายงาน/dashboard (Phase 5)

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
